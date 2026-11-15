namespace Ressoa.Telemetria.Api.Domain;

/// <summary>Status diário enviado pelo aparelho (bateria, temperatura da água e resultado do pulso).</summary>
public class Leitura
{
    public long Id { get; set; }
    public long DispositivoId { get; set; }
    public Dispositivo? Dispositivo { get; set; }

    /// <summary>Valor do header Idempotency-Key. Único no banco: reenvios não duplicam a leitura.</summary>
    public string ChaveIdempotencia { get; set; } = string.Empty;

    public DateTime RegistradaEm { get; set; }
    public int BateriaPct { get; set; }
    public decimal TemperaturaAgua { get; set; }
    public bool PulsoOk { get; set; }

    /// <summary>
    /// Duas requisições com a mesma chave só são a "mesma operação" se o conteúdo for igual.
    /// </summary>
    public bool MesmoConteudo(long dispositivoId, int bateriaPct, decimal temperaturaAgua, bool pulsoOk) =>
        DispositivoId == dispositivoId
        && BateriaPct == bateriaPct
        && decimal.Round(TemperaturaAgua, 1) == decimal.Round(temperaturaAgua, 1)
        && PulsoOk == pulsoOk;
}
