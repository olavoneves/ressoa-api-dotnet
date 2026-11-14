using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Ressoa.Telemetria.Api.Application;
using Ressoa.Telemetria.Api.Background;
using Ressoa.Telemetria.Api.Data;
using Ressoa.Telemetria.Api.Endpoints;
using Ressoa.Telemetria.Api.Infra.Auth;
using Ressoa.Telemetria.Api.Infra.Health;
using Ressoa.Telemetria.Api.Infra.OpenApi;
using Ressoa.Telemetria.Api.Infra.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Banco Oracle (mesmo esquema da API Java). Connection string só por variável de ambiente:
// ConnectionStrings__Oracle="User Id=...;Password=...;Data Source=host:1521/servico"
builder.Services.AddDbContext<RessoaDbContext>(o =>
{
    var conexao = builder.Configuration.GetConnectionString("Oracle");
    if (!string.IsNullOrWhiteSpace(conexao))
    {
        o.UseOracle(conexao);
    }
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<LeituraService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddAutenticacaoRessoa(builder.Configuration);
builder.Services.AddLimiteRequisicoes(builder.Configuration);
builder.Services.AddSaude(builder.Configuration);
builder.Services.AddDocumentacao();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders(TelemetriaEndpoints.HeaderRepetida, "Retry-After")));
if (builder.Configuration.GetValue("MonitorSinal:Habilitado", true))
{
    builder.Services.AddHostedService<MonitorSinalService>();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDocumentacao();
app.MapSaude();
app.MapTelemetria();

app.Run();

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
