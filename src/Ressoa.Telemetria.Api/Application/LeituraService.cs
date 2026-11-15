using Microsoft.EntityFrameworkCore;
using Ressoa.Telemetria.Api.Contracts;
using Ressoa.Telemetria.Api.Data;
using Ressoa.Telemetria.Api.Domain;
using Ressoa.Telemetria.Api.Infra.Auth;

namespace Ressoa.Telemetria.Api.Application;

/// <summary>
/// Registro de telemetria com idempotência:
/// <list type="bullet">
/// <item>1ª requisição com uma Idempotency-Key → grava a leitura (201).</item>
/// <item>Mesma chave e mesmo conteúdo → devolve a leitura original, sem gravar de novo (200 + Idempotency-Replayed).</item>
/// <item>Mesma chave com conteúdo diferente → 422 (uso indevido da chave).</item>
/// <item>Corrida entre duas requisições simultâneas → a UK CHAVE_IDEMPOTENCIA do banco garante uma só gravação.</item>
/// </list>
/// </summary>
public class LeituraService(RessoaDbContext db, TimeProvider relogio, ILogger<LeituraService> log)
{
    public const int TamanhoMaximoChave = 64;

    public async Task<Resultado<LeituraResponse>> RegistrarAsync(LeituraRequest req, string chave,
        UsuarioAtual usuario, CancellationToken ct)
    {
        var dispositivo = await BuscarDispositivoAsync(req, ct);
        if (dispositivo is null)
        {
            return Resultado<LeituraResponse>.Erro(TipoFalha.NaoEncontrado, "Aparelho não encontrado.");
        }
        if (!usuario.EhGestor && !dispositivo.PertenceA(usuario.Id))
        {
            return Resultado<LeituraResponse>.Erro(TipoFalha.Proibido, "Este aparelho pertence a outro morador.");
        }

        var existente = await db.Leituras.AsNoTracking().FirstOrDefaultAsync(l => l.ChaveIdempotencia == chave, ct);
        if (existente is not null)
        {
            return Repeticao(existente, dispositivo, req);
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        var registradaEm = req.RegistradaEm?.ToUniversalTime() ?? agora;
        var erroHorario = RegrasTelemetria.ValidarHorario(registradaEm, agora);
        if (erroHorario is not null)
        {
            return Resultado<LeituraResponse>.Erro(TipoFalha.Invalido, erroHorario);
        }

        var leitura = new Leitura
        {
            DispositivoId = dispositivo.Id,
            ChaveIdempotencia = chave,
            RegistradaEm = registradaEm,
            BateriaPct = req.BateriaPct!.Value,
            TemperaturaAgua = decimal.Round(req.TemperaturaAgua!.Value, 1),
            PulsoOk = req.PulsoOk!.Value
        };
        db.Leituras.Add(leitura);

        // Só a leitura mais recente define o status (leituras offline antigas não regridem o estado).
        var maisRecente = await db.Leituras.Where(l => l.DispositivoId == dispositivo.Id)
            .MaxAsync(l => (DateTime?)l.RegistradaEm, ct);
        if (maisRecente is null || registradaEm >= maisRecente)
        {
            dispositivo.Status = RegrasTelemetria.StatusAposLeitura(dispositivo.Status, leitura.BateriaPct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Outra requisição com a mesma chave gravou primeiro (corrida): trata como repetição.
            log.LogInformation(ex, "Conflito de idempotência para a chave {Chave}", chave);
            db.ChangeTracker.Clear();
            var vencedora = await db.Leituras.AsNoTracking().FirstOrDefaultAsync(l => l.ChaveIdempotencia == chave, ct);
            if (vencedora is null)
            {
                throw;
            }
            var atual = await db.Dispositivos.AsNoTracking().FirstAsync(d => d.Id == dispositivo.Id, ct);
            return Repeticao(vencedora, atual, req);
        }

        return Resultado<LeituraResponse>.Ok(LeituraResponse.De(leitura, dispositivo));
    }

    public async Task<Resultado<PageResponse<LeituraResponse>>> HistoricoAsync(long dispositivoId, UsuarioAtual usuario,
        int page, int size, DateTime? de, DateTime? ate, CancellationToken ct)
    {
        var dispositivo = await db.Dispositivos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dispositivoId, ct);
        var acesso = VerificarAcesso<PageResponse<LeituraResponse>>(dispositivo, usuario);
        if (acesso is not null)
        {
            return acesso;
        }

        var consulta = db.Leituras.AsNoTracking().Where(l => l.DispositivoId == dispositivoId);
        if (de is not null)
        {
            var deUtc = de.Value.ToUniversalTime();
            consulta = consulta.Where(l => l.RegistradaEm >= deUtc);
        }
        if (ate is not null)
        {
            var ateUtc = ate.Value.ToUniversalTime();
            consulta = consulta.Where(l => l.RegistradaEm <= ateUtc);
        }

        var total = await consulta.LongCountAsync(ct);
        var itens = await consulta.OrderByDescending(l => l.RegistradaEm).ThenByDescending(l => l.Id)
            .Skip(page * size).Take(size).ToListAsync(ct);
        var conteudo = itens.Select(l => LeituraResponse.De(l, dispositivo!)).ToList();
        return Resultado<PageResponse<LeituraResponse>>.Ok(PageResponse<LeituraResponse>.Of(conteudo, page, size, total));
    }

    public async Task<Resultado<StatusDispositivoResponse>> StatusAsync(long dispositivoId, UsuarioAtual usuario,
        CancellationToken ct)
    {
        var dispositivo = await db.Dispositivos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dispositivoId, ct);
        var acesso = VerificarAcesso<StatusDispositivoResponse>(dispositivo, usuario);
        if (acesso is not null)
        {
            return acesso;
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        var seteDiasAtras = agora.AddDays(-7);
        var ultima = await db.Leituras.AsNoTracking().Where(l => l.DispositivoId == dispositivoId)
            .OrderByDescending(l => l.RegistradaEm).ThenByDescending(l => l.Id).FirstOrDefaultAsync(ct);
        var pulsos7Dias = await db.Leituras.AsNoTracking()
            .Where(l => l.DispositivoId == dispositivoId && l.RegistradaEm >= seteDiasAtras)
            .Select(l => l.PulsoOk).ToListAsync(ct);

        var d = dispositivo!;
        return Resultado<StatusDispositivoResponse>.Ok(new StatusDispositivoResponse(
            d.Id, d.NumeroSerie, d.Status,
            RegrasTelemetria.EstaOnline(ultima?.RegistradaEm, agora),
            ultima?.RegistradaEm, ultima?.BateriaPct, ultima?.TemperaturaAgua, ultima?.PulsoOk,
            d.PulsosPorDia,
            RegrasTelemetria.TaxaSucessoPulso(pulsos7Dias),
            pulsos7Dias.Count,
            RegrasTelemetria.Alertas(d.Status, ultima, agora)));
    }

    private Task<Dispositivo?> BuscarDispositivoAsync(LeituraRequest req, CancellationToken ct)
    {
        if (req.DispositivoId is not null)
        {
            return db.Dispositivos.FirstOrDefaultAsync(d => d.Id == req.DispositivoId, ct);
        }
        var serie = req.NumeroSerie!.Trim().ToUpperInvariant();
        return db.Dispositivos.FirstOrDefaultAsync(d => d.NumeroSerie == serie, ct);
    }

    private static Resultado<LeituraResponse> Repeticao(Leitura existente, Dispositivo dispositivo, LeituraRequest req)
    {
        if (!existente.MesmoConteudo(dispositivo.Id, req.BateriaPct!.Value, req.TemperaturaAgua!.Value, req.PulsoOk!.Value))
        {
            return Resultado<LeituraResponse>.Erro(TipoFalha.ConflitoIdempotencia,
                "Esta Idempotency-Key já foi usada com outro conteúdo. Gere uma nova chave para uma nova leitura.");
        }
        return Resultado<LeituraResponse>.Ok(LeituraResponse.De(existente, dispositivo), repetido: true);
    }

    private static Resultado<T>? VerificarAcesso<T>(Dispositivo? dispositivo, UsuarioAtual usuario)
    {
        // 404 também quando o aparelho é de outro morador, para não revelar sua existência
        if (dispositivo is null || (!usuario.EhGestor && !dispositivo.PertenceA(usuario.Id)))
        {
            return Resultado<T>.Erro(TipoFalha.NaoEncontrado, "Aparelho não encontrado.");
        }
        return null;
    }
}
