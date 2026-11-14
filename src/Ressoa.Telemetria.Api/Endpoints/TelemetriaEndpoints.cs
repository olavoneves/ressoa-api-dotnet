using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Ressoa.Telemetria.Api.Application;
using Ressoa.Telemetria.Api.Contracts;
using Ressoa.Telemetria.Api.Infra.Auth;
using Ressoa.Telemetria.Api.Infra.RateLimiting;

namespace Ressoa.Telemetria.Api.Endpoints;

public static class TelemetriaEndpoints
{
    public const string HeaderIdempotencia = "Idempotency-Key";
    public const string HeaderRepetida = "Idempotency-Replayed";
    private const int TamanhoMaximoPagina = 100;

    public static IEndpointRouteBuilder MapTelemetria(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1")
            .RequireAuthorization(Politicas.Autenticado)
            .WithTags("Telemetria")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        v1.MapPost("/leituras", RegistrarLeitura)
            .RequireRateLimiting(LimiteRequisicoesExtensions.PoliticaTelemetria)
            .WithName("RegistrarLeitura")
            .WithSummary("Registra o status enviado pelo aparelho (idempotente)")
            .WithDescription("Envie o header Idempotency-Key. Reenvio com a mesma chave e o mesmo corpo devolve 200 com a leitura original e o header Idempotency-Replayed: true; com corpo diferente devolve 422.")
            .Produces<LeituraResponse>(StatusCodes.Status201Created)
            .Produces<LeituraResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        v1.MapGet("/dispositivos/{id:long}/leituras", Historico)
            .RequireRateLimiting(LimiteRequisicoesExtensions.PoliticaConsulta)
            .WithName("HistoricoLeituras")
            .WithSummary("Histórico paginado de leituras do aparelho (mais recentes primeiro)")
            .Produces<PageResponse<LeituraResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        v1.MapGet("/dispositivos/{id:long}/status", Status)
            .RequireRateLimiting(LimiteRequisicoesExtensions.PoliticaConsulta)
            .WithName("StatusDispositivo")
            .WithSummary("Último status do aparelho com indicadores e alertas para o morador")
            .Produces<StatusDispositivoResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> RegistrarLeitura(
        LeituraRequest req,
        [FromHeader(Name = HeaderIdempotencia)] [Description("Chave única da leitura (ex.: UUID), até 64 caracteres")] string? chave,
        ClaimsPrincipal user, LeituraService service, HttpContext http, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length > LeituraService.TamanhoMaximoChave)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [HeaderIdempotencia] = ["O header Idempotency-Key é obrigatório e deve ter até 64 caracteres."]
                },
                title: "Dados inválidos");
        }

        var resultado = await service.RegistrarAsync(req, chave.Trim(), UsuarioAtual.De(user), ct);
        if (!resultado.Sucesso)
        {
            return Problema(resultado.Falha!);
        }
        if (resultado.Repetido)
        {
            http.Response.Headers[HeaderRepetida] = "true";
            return Results.Ok(resultado.Valor);
        }
        return Results.Created($"/api/v1/dispositivos/{resultado.Valor!.DispositivoId}/leituras", resultado.Valor);
    }

    private static async Task<IResult> Historico(long id, ClaimsPrincipal user, LeituraService service,
        CancellationToken ct, int page = 0, int size = 20, DateTime? de = null, DateTime? ate = null)
    {
        if (page < 0 || size < 1 || size > TamanhoMaximoPagina)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["paginacao"] = [$"page deve ser >= 0 e size entre 1 e {TamanhoMaximoPagina}."]
            }, title: "Dados inválidos");
        }
        var resultado = await service.HistoricoAsync(id, UsuarioAtual.De(user), page, size, de, ate, ct);
        return resultado.Sucesso ? Results.Ok(resultado.Valor) : Problema(resultado.Falha!);
    }

    private static async Task<IResult> Status(long id, ClaimsPrincipal user, LeituraService service, CancellationToken ct)
    {
        var resultado = await service.StatusAsync(id, UsuarioAtual.De(user), ct);
        return resultado.Sucesso ? Results.Ok(resultado.Valor) : Problema(resultado.Falha!);
    }

    private static IResult Problema(Falha falha) => falha.Tipo switch
    {
        TipoFalha.NaoEncontrado => Results.Problem(falha.Mensagem, statusCode: 404, title: "Recurso não encontrado",
            type: "https://ressoa.app/erros/nao-encontrado"),
        TipoFalha.Proibido => Results.Problem(falha.Mensagem, statusCode: 403, title: "Acesso negado",
            type: "https://ressoa.app/erros/acesso-negado"),
        TipoFalha.ConflitoIdempotencia => Results.Problem(falha.Mensagem, statusCode: 422,
            title: "Chave de idempotência reutilizada", type: "https://ressoa.app/erros/idempotencia"),
        _ => Results.Problem(falha.Mensagem, statusCode: 422, title: "Regra de negócio violada",
            type: "https://ressoa.app/erros/regra-negocio")
    };
}
