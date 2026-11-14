using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ressoa.Telemetria.Api.Contracts;
using Ressoa.Telemetria.Api.Domain;

namespace Ressoa.Telemetria.IntegrationTests;

[Collection(nameof(ColecaoApi))]
public sealed class LeiturasEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly RessoaApiFactory _factory;

    public LeiturasEndpointsTests(RessoaApiFactory factory)
    {
        _factory = factory;
        _factory.ReiniciarBanco();
    }

    private static HttpRequestMessage Post(object corpo, string? chave)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/leituras") { Content = JsonContent.Create(corpo) };
        if (chave is not null)
        {
            req.Headers.Add("Idempotency-Key", chave);
        }
        return req;
    }

    private static object Corpo(string serie = "RSA-2026-0001", int bateria = 80, decimal temperatura = 24.5m,
        bool pulso = true) =>
        new { numeroSerie = serie, bateriaPct = bateria, temperaturaAgua = temperatura, pulsoOk = pulso };

    [Fact]
    public async Task Registra_leitura_e_devolve_201()
    {
        var cliente = _factory.ClienteComo(RessoaApiFactory.MoradorId);

        var resposta = await cliente.SendAsync(Post(Corpo(), Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var leitura = await resposta.Content.ReadFromJsonAsync<LeituraResponse>(Json);
        Assert.Equal(1, leitura!.DispositivoId);
        Assert.Equal(StatusDispositivo.ATIVO, leitura.StatusDispositivo);
    }

    [Fact]
    public async Task Repeticao_com_mesma_chave_nao_duplica_e_marca_header()
    {
        var cliente = _factory.ClienteComo(RessoaApiFactory.MoradorId);
        var chave = Guid.NewGuid().ToString();

        var primeira = await cliente.SendAsync(Post(Corpo(), chave));
        var segunda = await cliente.SendAsync(Post(Corpo(), chave));

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal("true", segunda.Headers.GetValues("Idempotency-Replayed").Single());
        var a = await primeira.Content.ReadFromJsonAsync<LeituraResponse>(Json);
        var b = await segunda.Content.ReadFromJsonAsync<LeituraResponse>(Json);
        Assert.Equal(a!.Id, b!.Id);

        var historico = await cliente.GetFromJsonAsync<PageResponse<LeituraResponse>>("/api/v1/dispositivos/1/leituras", Json);
        Assert.Equal(1, historico!.TotalElements);
    }

    [Fact]
    public async Task Mesma_chave_com_conteudo_diferente_devolve_422()
    {
        var cliente = _factory.ClienteComo(RessoaApiFactory.MoradorId);
        var chave = Guid.NewGuid().ToString();
        await cliente.SendAsync(Post(Corpo(bateria: 80), chave));

        var resposta = await cliente.SendAsync(Post(Corpo(bateria: 50), chave));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Sem_header_de_idempotencia_devolve_400()
    {
        var resposta = await _factory.ClienteComo(RessoaApiFactory.MoradorId).SendAsync(Post(Corpo(), null));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Corpo_invalido_devolve_400_com_erros_de_validacao()
    {
        var cliente = _factory.ClienteComo(RessoaApiFactory.MoradorId);

        var faixa = await cliente.SendAsync(Post(Corpo(bateria: 150), Guid.NewGuid().ToString()));
        var semAparelho = await cliente.SendAsync(
            Post(new { bateriaPct = 80, temperaturaAgua = 24.5, pulsoOk = true }, Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.BadRequest, faixa.StatusCode);
        Assert.Contains("BateriaPct", await faixa.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, semAparelho.StatusCode);
        Assert.Contains("numeroSerie ou dispositivoId", await semAparelho.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Bateria_baixa_altera_status_do_aparelho()
    {
        var cliente = _factory.ClienteComo(RessoaApiFactory.MoradorId);

        await cliente.SendAsync(Post(Corpo(bateria: 9, pulso: false), Guid.NewGuid().ToString()));
        var status = await cliente.GetFromJsonAsync<StatusDispositivoResponse>("/api/v1/dispositivos/1/status", Json);

        Assert.Equal(StatusDispositivo.BATERIA_BAIXA, status!.Status);
        Assert.True(status.Online);
        Assert.Equal(9, status.BateriaPct);
        Assert.Equal(0m, status.TaxaSucessoPulso7Dias);
        Assert.Contains(status.Alertas, a => a.Contains("Bateria em 9%"));
    }

    [Fact]
    public async Task Morador_nao_registra_nem_consulta_aparelho_de_outro()
    {
        var cliente = _factory.ClienteComo(RessoaApiFactory.MoradorId);

        var post = await cliente.SendAsync(Post(Corpo(serie: "RSA-2026-0003"), Guid.NewGuid().ToString()));
        var get = await cliente.GetAsync("/api/v1/dispositivos/3/status");

        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Gestor_consulta_qualquer_aparelho()
    {
        var resposta = await _factory.ClienteComo(RessoaApiFactory.GestorId, "GESTOR").GetAsync("/api/v1/dispositivos/3/status");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Aparelho_inexistente_devolve_404()
    {
        var resposta = await _factory.ClienteComo(RessoaApiFactory.MoradorId)
            .SendAsync(Post(Corpo(serie: "RSA-NAO-EXISTE"), Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Historico_e_paginado()
    {
        var cliente = _factory.ClienteComo(RessoaApiFactory.MoradorId);
        for (var i = 0; i < 3; i++)
        {
            await cliente.SendAsync(Post(new
            {
                dispositivoId = 2,
                bateriaPct = 90 - i,
                temperaturaAgua = 25.0,
                pulsoOk = true,
                registradaEm = DateTime.UtcNow.AddHours(-i)
            }, Guid.NewGuid().ToString()));
        }

        var pagina = await cliente.GetFromJsonAsync<PageResponse<LeituraResponse>>(
            "/api/v1/dispositivos/2/leituras?page=0&size=2", Json);

        Assert.Equal(3, pagina!.TotalElements);
        Assert.Equal(2, pagina.Content.Count);
        Assert.Equal(2, pagina.TotalPages);
        Assert.Equal(90, pagina.Content[0].BateriaPct);
    }
}
