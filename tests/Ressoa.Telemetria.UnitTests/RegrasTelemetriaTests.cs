using Ressoa.Telemetria.Api.Domain;

namespace Ressoa.Telemetria.UnitTests;

public class RegrasTelemetriaTests
{
    private static readonly DateTime Agora = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(StatusDispositivo.ATIVO, 80, StatusDispositivo.ATIVO)]
    [InlineData(StatusDispositivo.ATIVO, 14, StatusDispositivo.BATERIA_BAIXA)]
    [InlineData(StatusDispositivo.ATIVO, 15, StatusDispositivo.ATIVO)]
    [InlineData(StatusDispositivo.BATERIA_BAIXA, 90, StatusDispositivo.ATIVO)]
    [InlineData(StatusDispositivo.SEM_SINAL, 50, StatusDispositivo.ATIVO)]
    [InlineData(StatusDispositivo.SEM_SINAL, 5, StatusDispositivo.BATERIA_BAIXA)]
    [InlineData(StatusDispositivo.INATIVO, 5, StatusDispositivo.INATIVO)]
    public void StatusAposLeitura_aplica_regras_de_bateria_e_reativacao(StatusDispositivo atual, int bateria,
        StatusDispositivo esperado)
    {
        Assert.Equal(esperado, RegrasTelemetria.StatusAposLeitura(atual, bateria));
    }

    [Fact]
    public void EstaOnline_considera_janela_de_26_horas()
    {
        Assert.True(RegrasTelemetria.EstaOnline(Agora.AddHours(-25), Agora));
        Assert.True(RegrasTelemetria.EstaOnline(Agora.AddHours(-26), Agora));
        Assert.False(RegrasTelemetria.EstaOnline(Agora.AddHours(-27), Agora));
        Assert.False(RegrasTelemetria.EstaOnline(null, Agora));
    }

    [Fact]
    public void ValidarHorario_aceita_leituras_offline_de_ate_7_dias_e_rejeita_futuro()
    {
        Assert.Null(RegrasTelemetria.ValidarHorario(Agora, Agora));
        Assert.Null(RegrasTelemetria.ValidarHorario(Agora.AddDays(-6), Agora));
        Assert.Null(RegrasTelemetria.ValidarHorario(Agora.AddMinutes(4), Agora));
        Assert.NotNull(RegrasTelemetria.ValidarHorario(Agora.AddDays(-8), Agora));
        Assert.NotNull(RegrasTelemetria.ValidarHorario(Agora.AddHours(1), Agora));
    }

    [Fact]
    public void Alertas_listam_bateria_pulso_sinal_e_temperatura()
    {
        var ultima = new Leitura
        {
            RegistradaEm = Agora.AddHours(-30),
            BateriaPct = 10,
            PulsoOk = false,
            TemperaturaAgua = 31.5m
        };

        var alertas = RegrasTelemetria.Alertas(StatusDispositivo.BATERIA_BAIXA, ultima, Agora);

        Assert.Equal(4, alertas.Count);
        Assert.Contains(alertas, a => a.Contains("Sem sinal há 30 horas"));
        Assert.Contains(alertas, a => a.Contains("Bateria em 10%"));
        Assert.Contains(alertas, a => a.Contains("pulso"));
        Assert.Contains(alertas, a => a.Contains("Água quente"));
    }

    [Fact]
    public void Alertas_de_aparelho_saudavel_sao_vazios_e_inativo_tem_alerta_unico()
    {
        var ultima = new Leitura { RegistradaEm = Agora.AddHours(-2), BateriaPct = 90, PulsoOk = true, TemperaturaAgua = 24m };

        Assert.Empty(RegrasTelemetria.Alertas(StatusDispositivo.ATIVO, ultima, Agora));
        Assert.Single(RegrasTelemetria.Alertas(StatusDispositivo.INATIVO, ultima, Agora));
        Assert.Single(RegrasTelemetria.Alertas(StatusDispositivo.ATIVO, null, Agora));
    }

    [Fact]
    public void TaxaSucessoPulso_calcula_percentual()
    {
        Assert.Equal(75.0m, RegrasTelemetria.TaxaSucessoPulso([true, true, true, false]));
        Assert.Null(RegrasTelemetria.TaxaSucessoPulso([]));
    }

    [Fact]
    public void MesmoConteudo_compara_payload_da_idempotencia()
    {
        var leitura = new Leitura { DispositivoId = 1, BateriaPct = 80, TemperaturaAgua = 24.5m, PulsoOk = true };

        Assert.True(leitura.MesmoConteudo(1, 80, 24.50m, true));
        Assert.False(leitura.MesmoConteudo(1, 79, 24.5m, true));
        Assert.False(leitura.MesmoConteudo(2, 80, 24.5m, true));
        Assert.False(leitura.MesmoConteudo(1, 80, 24.5m, false));
    }
}
