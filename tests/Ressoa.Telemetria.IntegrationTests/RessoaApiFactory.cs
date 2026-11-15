using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Ressoa.Telemetria.Api.Data;
using Ressoa.Telemetria.Api.Domain;

namespace Ressoa.Telemetria.IntegrationTests;

/// <summary>
/// Sobe a API real em memória, trocando o Oracle por SQLite in-memory e emitindo JWTs com o
/// mesmo formato da API Java (claims sub, email, papel; issuer e audience).
/// </summary>
public class RessoaApiFactory : WebApplicationFactory<Program>
{
    public const string Segredo = "segredo-de-teste-com-pelo-menos-32-bytes-123456";
    public const long MoradorId = 10;
    public const long OutroMoradorId = 11;
    public const long GestorId = 1;

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");

    /// <summary>Limite alto por padrão para os testes funcionais; a subclasse limitada testa o 429.</summary>
    protected virtual int LimiteTelemetria => 1000;

    public RessoaApiFactory()
    {
        _conexao.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("JWT_SECRET", Segredo);
        builder.UseSetting("MonitorSinal:Habilitado", "false");
        builder.UseSetting("LimiteRequisicoes:TelemetriaPermitidas", LimiteTelemetria.ToString());
        builder.UseSetting("LimiteRequisicoes:TelemetriaJanelaSegundos", "60");
        builder.UseSetting("Servicos:ApiJavaHealthUrl", "http://127.0.0.1:1/actuator/health");
        builder.UseSetting("Servicos:InfoDengueUrl", "http://127.0.0.1:1/infodengue");
        builder.ConfigureServices(services =>
            services.AddDbContext<RessoaDbContext>(o => o.UseSqlite(_conexao)));
    }

    /// <summary>Recria o banco com 3 aparelhos: 2 do morador e 1 do outro morador.</summary>
    public void ReiniciarBanco()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RessoaDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
        var instalado = DateTime.UtcNow.AddDays(-30);
        db.Dispositivos.AddRange(
            Novo(1, "RSA-2026-0001", MoradorId, instalado),
            Novo(2, "RSA-2026-0002", MoradorId, instalado),
            Novo(3, "RSA-2026-0003", OutroMoradorId, instalado));
        db.SaveChanges();
    }

    public HttpClient ClienteComo(long usuarioId, string papel = "CIDADAO")
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(usuarioId, papel));
        return cliente;
    }

    public static string Token(long usuarioId, string papel, string segredo = Segredo, string emissor = "ressoa-api",
        DateTime? expira = null)
    {
        var descritor = new SecurityTokenDescriptor
        {
            Issuer = emissor,
            Audience = "ressoa",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", usuarioId.ToString()),
                new Claim("email", $"usuario{usuarioId}@teste.com"),
                new Claim("papel", papel)
            ]),
            NotBefore = (expira ?? DateTime.UtcNow.AddHours(1)).AddHours(-2),
            Expires = expira ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(segredo)),
                SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descritor);
    }

    private static Dispositivo Novo(long id, string serie, long usuarioId, DateTime instalado) => new()
    {
        Id = id,
        NumeroSerie = serie,
        UsuarioId = usuarioId,
        BairroId = 1,
        TipoRecipiente = "TAMBOR",
        VolumeLitros = 200,
        PulsosPorDia = 1,
        Status = StatusDispositivo.ATIVO,
        InstaladoEm = instalado
    };

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _conexao.Dispose();
        }
    }
}

/// <summary>Instância com limite de 5 leituras por minuto para validar o HTTP 429.</summary>
public sealed class RessoaApiFactoryLimitada : RessoaApiFactory
{
    protected override int LimiteTelemetria => 5;
}
