namespace Ressoa.Telemetria.Api.Domain;

/// <summary>
/// Regras de negócio da telemetria, puras e testáveis (sem banco nem HTTP).
/// </summary>
public static class RegrasTelemetria
{
    /// <summary>Abaixo deste percentual o aparelho fica BATERIA_BAIXA (mesma regra do gatilho trg_bateria_baixa).</summary>
    public const int LimiteBateriaBaixaPct = 15;

    /// <summary>O pulso é diário: sem status por mais de 26 h (1 dia + margem), o aparelho está sem sinal.</summary>
    public static readonly TimeSpan JanelaSinal = TimeSpan.FromHours(26);

    /// <summary>Água acima desta temperatura acelera o ciclo da larva (alerta ao morador).</summary>
    public const decimal TemperaturaAlertaC = 30m;

    /// <summary>Aparelho funciona offline e envia leituras acumuladas: aceitamos até 7 dias no passado.</summary>
    public static readonly TimeSpan AtrasoMaximo = TimeSpan.FromDays(7);

    /// <summary>Tolerância para relógio do ESP32 adiantado.</summary>
    public static readonly TimeSpan AdiantamentoMaximo = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Novo status do aparelho após uma leitura. INATIVO (desligado pelo dono) não muda;
    /// bateria baixa tem prioridade; qualquer leitura com bateria boa reativa o aparelho.
    /// </summary>
    public static StatusDispositivo StatusAposLeitura(StatusDispositivo atual, int bateriaPct)
    {
        if (atual == StatusDispositivo.INATIVO)
        {
            return StatusDispositivo.INATIVO;
        }
        return bateriaPct < LimiteBateriaBaixaPct ? StatusDispositivo.BATERIA_BAIXA : StatusDispositivo.ATIVO;
    }

    public static bool EstaOnline(DateTime? ultimaLeituraUtc, DateTime agoraUtc) =>
        ultimaLeituraUtc is not null && agoraUtc - ultimaLeituraUtc.Value <= JanelaSinal;

    /// <summary>Retorna a mensagem de erro se o horário informado pelo aparelho estiver fora da janela aceita.</summary>
    public static string? ValidarHorario(DateTime registradaEmUtc, DateTime agoraUtc)
    {
        if (registradaEmUtc > agoraUtc + AdiantamentoMaximo)
        {
            return "registradaEm não pode estar no futuro.";
        }
        if (agoraUtc - registradaEmUtc > AtrasoMaximo)
        {
            return "registradaEm não pode ter mais de 7 dias.";
        }
        return null;
    }

    /// <summary>Alertas em linguagem simples para o app, a partir do último status.</summary>
    public static IReadOnlyList<string> Alertas(StatusDispositivo status, Leitura? ultima, DateTime agoraUtc)
    {
        var alertas = new List<string>();
        if (status == StatusDispositivo.INATIVO)
        {
            alertas.Add("Aparelho desligado: a água deste recipiente não está protegida.");
            return alertas;
        }
        if (ultima is null)
        {
            alertas.Add("O aparelho ainda não enviou nenhum status. Verifique a instalação e o Wi-Fi.");
            return alertas;
        }
        if (!EstaOnline(ultima.RegistradaEm, agoraUtc))
        {
            var horas = (int)(agoraUtc - ultima.RegistradaEm).TotalHours;
            alertas.Add($"Sem sinal há {horas} horas. O pulso diário continua, mas verifique o Wi-Fi.");
        }
        if (ultima.BateriaPct < LimiteBateriaBaixaPct)
        {
            alertas.Add($"Bateria em {ultima.BateriaPct}%: recarregue ou troque a bateria.");
        }
        if (!ultima.PulsoOk)
        {
            alertas.Add("O último pulso de ultrassom falhou. Confira se o aparelho está submerso.");
        }
        if (ultima.TemperaturaAgua >= TemperaturaAlertaC)
        {
            alertas.Add("Água quente acelera o ciclo da larva: mantenha o recipiente tampado e à sombra.");
        }
        return alertas;
    }

    /// <summary>Percentual de pulsos bem-sucedidos (0 a 100), ou null sem leituras.</summary>
    public static decimal? TaxaSucessoPulso(IReadOnlyCollection<bool> pulsos) =>
        pulsos.Count == 0 ? null : decimal.Round(100m * pulsos.Count(p => p) / pulsos.Count, 1);
}
