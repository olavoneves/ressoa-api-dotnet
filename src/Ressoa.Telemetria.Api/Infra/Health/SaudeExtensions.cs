using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ressoa.Telemetria.Api.Data;

namespace Ressoa.Telemetria.Api.Infra.Health;

public static class SaudeExtensions
{
    public const string TagPronto = "pronto";
    public const string TagExterno = "externo";

    /// <summary>
    /// Health checks da API, do banco Oracle e dos serviços externos (API Java e InfoDengue).
    /// Serviços externos indisponíveis deixam a API "Degraded", não "Unhealthy": a telemetria continua funcionando.
    /// </summary>
    public static IServiceCollection AddSaude(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient(UrlHealthCheck.NomeCliente, c => c.Timeout = TimeSpan.FromSeconds(5));

        var urlJava = config["Servicos:ApiJavaHealthUrl"] ?? "http://localhost:8080/actuator/health";
        var urlInfoDengue = config["Servicos:InfoDengueUrl"]
                            ?? "https://info.dengue.mat.br/api/alertcity?geocode=3550308&disease=dengue&format=json&ew_start=1&ew_end=1&ey_start=2025&ey_end=2025";

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("API em execução"), tags: ["vivo"])
            .AddDbContextCheck<RessoaDbContext>("oracle", HealthStatus.Unhealthy, [TagPronto])
            .AddTypeActivatedCheck<UrlHealthCheck>("api-java", HealthStatus.Degraded, [TagExterno], urlJava)
            .AddTypeActivatedCheck<UrlHealthCheck>("infodengue", HealthStatus.Degraded, [TagExterno], urlInfoDengue);
        return services;
    }

    public static WebApplication MapSaude(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = EscreverJson });
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains("vivo"),
            ResponseWriter = EscreverJson
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains(TagPronto),
            ResponseWriter = EscreverJson
        });
        return app;
    }

    private static Task EscreverJson(HttpContext ctx, HealthReport relatorio)
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var corpo = new
        {
            status = relatorio.Status.ToString(),
            duracaoMs = Math.Round(relatorio.TotalDuration.TotalMilliseconds, 1),
            verificacoes = relatorio.Entries.Select(e => new
            {
                nome = e.Key,
                status = e.Value.Status.ToString(),
                descricao = e.Value.Description,
                duracaoMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                tags = e.Value.Tags
            })
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(corpo, JsonSerializerOptions.Web));
    }
}
