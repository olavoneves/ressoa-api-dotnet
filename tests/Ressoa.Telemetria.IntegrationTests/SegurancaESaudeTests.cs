using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ressoa.Telemetria.IntegrationTests;

[Collection(nameof(ColecaoApi))]
public sealed class SegurancaESaudeTests
{
    private readonly RessoaApiFactory _factory;

    public SegurancaESaudeTests(RessoaApiFactory factory)
    {
        _factory = factory;
        _factory.ReiniciarBanco();
    }

    [Fact]
    public async Task Sem_token_devolve_401()
    {
        var resposta = await _factory.CreateClient().GetAsync("/api/v1/dispositivos/1/status");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Theory]
    [InlineData("assinatura")]
    [InlineData("emissor")]
    [InlineData("expirado")]
    public async Task Token_invalido_devolve_401(string defeito)
    {
        var token = defeito switch
        {
            "assinatura" => RessoaApiFactory.Token(10, "CIDADAO", segredo: "outro-segredo-com-mais-de-32-bytes-0000"),
            "emissor" => RessoaApiFactory.Token(10, "CIDADAO", emissor: "outra-api"),
            _ => RessoaApiFactory.Token(10, "CIDADAO", expira: DateTime.UtcNow.AddMinutes(-10))
        };
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resposta = await cliente.GetAsync("/api/v1/dispositivos/1/status");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Health_reporta_banco_e_servicos_externos()
    {
        var resposta = await _factory.CreateClient().GetAsync("/health");
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var verificacoes = json.RootElement.GetProperty("verificacoes").EnumerateArray()
            .ToDictionary(v => v.GetProperty("nome").GetString()!, v => v.GetProperty("status").GetString());

        Assert.Equal("Healthy", verificacoes["oracle"]);
        Assert.Equal("Degraded", verificacoes["api-java"]);
        Assert.Equal("Degraded", verificacoes["infodengue"]);
        Assert.Equal("Degraded", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Documentacao_openapi_esta_publicada()
    {
        var resposta = await _factory.CreateClient().GetAsync("/openapi/v1.json");
        var corpo = await resposta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Contains("/api/v1/leituras", corpo);
        Assert.Contains("Bearer", corpo);
    }
}

public sealed class LimiteRequisicoesTests : IClassFixture<RessoaApiFactoryLimitada>
{
    private readonly RessoaApiFactoryLimitada _factory;

    public LimiteRequisicoesTests(RessoaApiFactoryLimitada factory)
    {
        _factory = factory;
        _factory.ReiniciarBanco();
    }

    [Fact]
    public async Task Excesso_de_leituras_devolve_429_com_retry_after()
    {
        var cliente = _factory.ClienteComo(RessoaApiFactory.GestorId, "GESTOR");
        HttpResponseMessage? ultima = null;
        for (var i = 0; i < 6; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/leituras")
            {
                Content = JsonContent.Create(new { dispositivoId = 1, bateriaPct = 80, temperaturaAgua = 24.0, pulsoOk = true })
            };
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            ultima = await cliente.SendAsync(req);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, ultima!.StatusCode);
        Assert.True(ultima.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", ultima.Content.Headers.ContentType!.MediaType);
    }
}

[CollectionDefinition(nameof(ColecaoApi))]
public sealed class ColecaoApi : ICollectionFixture<RessoaApiFactory>;
