using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ressoa.Telemetria.Api.Infra.Health;

/// <summary>Verifica se um serviço HTTP externo responde com sucesso.</summary>
public sealed class UrlHealthCheck(IHttpClientFactory httpClientFactory, string url) : IHealthCheck
{
    public const string NomeCliente = "health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var resposta = await httpClientFactory.CreateClient(NomeCliente).GetAsync(url, ct);
            return resposta.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Respondeu HTTP {(int)resposta.StatusCode}")
                : new HealthCheckResult(context.Registration.FailureStatus,
                    $"Respondeu HTTP {(int)resposta.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Serviço inacessível", ex);
        }
    }
}
