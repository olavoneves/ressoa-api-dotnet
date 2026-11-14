using Microsoft.EntityFrameworkCore;
using Ressoa.Telemetria.Api.Data;
using Ressoa.Telemetria.Api.Domain;

namespace Ressoa.Telemetria.Api.Background;

/// <summary>
/// Tarefa em segundo plano: marca como SEM_SINAL os aparelhos ativos que não enviam status há mais
/// de 26 horas, para o gestor enxergar rapidamente onde a proteção pode ter parado.
/// </summary>
public sealed class MonitorSinalService(IServiceScopeFactory scopes, TimeProvider relogio, IConfiguration config,
    ILogger<MonitorSinalService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromMinutes(config.GetValue("MonitorSinal:IntervaloMinutos", 30));
        using var timer = new PeriodicTimer(intervalo, relogio);
        do
        {
            try
            {
                await VerificarAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Falha ao verificar aparelhos sem sinal");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> VerificarAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RessoaDbContext>();
        var limite = relogio.GetUtcNow().UtcDateTime - RegrasTelemetria.JanelaSinal;

        var semSinal = await db.Dispositivos
            .Where(d => d.Status == StatusDispositivo.ATIVO || d.Status == StatusDispositivo.BATERIA_BAIXA)
            .Where(d => d.InstaladoEm < limite)
            .Where(d => !db.Leituras.Any(l => l.DispositivoId == d.Id && l.RegistradaEm >= limite))
            .ToListAsync(ct);

        foreach (var dispositivo in semSinal)
        {
            dispositivo.Status = StatusDispositivo.SEM_SINAL;
        }
        if (semSinal.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("{Quantidade} aparelho(s) marcado(s) como SEM_SINAL", semSinal.Count);
        }
        return semSinal.Count;
    }
}
