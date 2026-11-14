namespace Ressoa.Telemetria.Api.Domain;

/// <summary>Situação operacional do aparelho (contrato do briefing).</summary>
public enum StatusDispositivo
{
    ATIVO,
    SEM_SINAL,
    BATERIA_BAIXA,
    INATIVO
}

/// <summary>
/// Aparelho Ressoa. O cadastro (CRUD) é da API Java; aqui só lemos os dados e atualizamos o status
/// conforme a telemetria.
/// </summary>
public class Dispositivo
{
    public long Id { get; set; }
    public string NumeroSerie { get; set; } = string.Empty;
    public long UsuarioId { get; set; }
    public long BairroId { get; set; }
    public string TipoRecipiente { get; set; } = string.Empty;
    public int VolumeLitros { get; set; }
    public int PulsosPorDia { get; set; }
    public StatusDispositivo Status { get; set; } = StatusDispositivo.ATIVO;
    public DateTime InstaladoEm { get; set; }

    public bool PertenceA(long usuarioId) => UsuarioId == usuarioId;
}
