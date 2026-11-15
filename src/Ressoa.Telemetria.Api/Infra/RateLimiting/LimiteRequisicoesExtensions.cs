using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ressoa.Telemetria.Api.Infra.RateLimiting;

public sealed class LimiteRequisicoesOptions
{
    /// <summary>Leituras aceitas por janela para cada usuário (o aparelho envia 1 por dia; folga para reenvios).</summary>
    public int TelemetriaPermitidas { get; set; } = 10;
    public int TelemetriaJanelaSegundos { get; set; } = 60;

    /// <summary>Limite geral de consultas por usuário/IP.</summary>
    public int ConsultaPermitidas { get; set; } = 60;
    public int ConsultaJanelaSegundos { get; set; } = 60;
}

public static class LimiteRequisicoesExtensions
{
    public const string PoliticaTelemetria = "telemetria";
    public const string PoliticaConsulta = "consulta";

    /// <summary>
    /// Rate limiting nativo do ASP.NET Core, particionado por usuário autenticado (claim sub) ou IP.
    /// Excesso devolve 429 em Problem Details com o header Retry-After.
    /// </summary>
    public static IServiceCollection AddLimiteRequisicoes(this IServiceCollection services, IConfiguration config)
    {
        var opcoes = config.GetSection("LimiteRequisicoes").Get<LimiteRequisicoesOptions>() ?? new LimiteRequisicoesOptions();

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            o.AddPolicy(PoliticaTelemetria, ctx => RateLimitPartition.GetFixedWindowLimiter(
                Particao(ctx),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = opcoes.TelemetriaPermitidas,
                    Window = TimeSpan.FromSeconds(opcoes.TelemetriaJanelaSegundos),
                    QueueLimit = 0
                }));

            o.AddPolicy(PoliticaConsulta, ctx => RateLimitPartition.GetSlidingWindowLimiter(
                Particao(ctx),
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = opcoes.ConsultaPermitidas,
                    Window = TimeSpan.FromSeconds(opcoes.ConsultaJanelaSegundos),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                }));

            o.OnRejected = async (contexto, ct) =>
            {
                var http = contexto.HttpContext;
                var segundos = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                    ? (int)Math.Ceiling(retry.TotalSeconds)
                    : opcoes.TelemetriaJanelaSegundos;
                http.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
                var problema = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Muitas requisições",
                    Detail = $"Limite de requisições excedido. Tente novamente em {segundos} segundos.",
                    Type = "https://ressoa.app/erros/limite-requisicoes",
                    Instance = http.Request.Path
                };
                await http.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problema });
            };
        });
        return services;
    }

    private static string Particao(HttpContext ctx) =>
        ctx.User.FindFirst("sub")?.Value is { } sub
            ? $"usuario:{sub}"
            : $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido"}";
}
