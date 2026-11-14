using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Ressoa.Telemetria.Api.Domain;

namespace Ressoa.Telemetria.Api.Contracts;

/// <summary>Status enviado pelo aparelho. Informe o número de série (padrão do ESP32) ou o id do aparelho.</summary>
public record LeituraRequest : IValidatableObject
{
    [Description("Número de série gravado no aparelho, ex.: RSA-2026-0001")]
    [StringLength(40, MinimumLength = 4)]
    public string? NumeroSerie { get; init; }

    [Description("Id do aparelho na plataforma (alternativa ao número de série)")]
    [Range(1, long.MaxValue)]
    public long? DispositivoId { get; init; }

    [Description("Carga da bateria, de 0 a 100")]
    [Required, Range(0, 100)]
    public int? BateriaPct { get; init; }

    [Description("Temperatura da água em °C")]
    [Required, Range(-10.0, 60.0)]
    public decimal? TemperaturaAgua { get; init; }

    [Description("Se o pulso de ultrassom do dia foi executado com sucesso")]
    [Required]
    public bool? PulsoOk { get; init; }

    [Description("Momento da medição (UTC). Opcional: padrão é o horário do servidor. Aceita até 7 dias atrás (modo offline)")]
    public DateTime? RegistradaEm { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(NumeroSerie) && DispositivoId is null)
        {
            yield return new ValidationResult("Informe numeroSerie ou dispositivoId.",
                [nameof(NumeroSerie), nameof(DispositivoId)]);
        }
    }
}

public record LeituraResponse(
    long Id,
    long DispositivoId,
    string NumeroSerie,
    DateTime RegistradaEm,
    int BateriaPct,
    decimal TemperaturaAgua,
    bool PulsoOk,
    StatusDispositivo StatusDispositivo)
{
    public static LeituraResponse De(Leitura l, Dispositivo d) =>
        new(l.Id, d.Id, d.NumeroSerie, l.RegistradaEm, l.BateriaPct, l.TemperaturaAgua, l.PulsoOk, d.Status);
}

/// <summary>Último status do aparelho, com indicadores derivados para a tela de detalhe do app.</summary>
public record StatusDispositivoResponse(
    long DispositivoId,
    string NumeroSerie,
    StatusDispositivo Status,
    bool Online,
    DateTime? UltimaLeituraEm,
    int? BateriaPct,
    decimal? TemperaturaAgua,
    bool? UltimoPulsoOk,
    int PulsosPorDia,
    decimal? TaxaSucessoPulso7Dias,
    int LeiturasUltimos7Dias,
    IReadOnlyList<string> Alertas);

/// <summary>Mesmo envelope de paginação da API Java.</summary>
public record PageResponse<T>(IReadOnlyList<T> Content, int Page, int Size, long TotalElements, int TotalPages, bool Last)
{
    public static PageResponse<T> Of(IReadOnlyList<T> content, int page, int size, long total)
    {
        var totalPages = size == 0 ? 0 : (int)Math.Ceiling(total / (double)size);
        return new PageResponse<T>(content, page, size, total, totalPages, page >= totalPages - 1);
    }
}
