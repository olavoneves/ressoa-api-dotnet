using Microsoft.OpenApi;

namespace Ressoa.Telemetria.Api.Infra.OpenApi;

public static class DocumentacaoExtensions
{
    /// <summary>OpenAPI nativo do .NET 10 com esquema Bearer (JWT emitido pela API Java).</summary>
    public static IServiceCollection AddDocumentacao(this IServiceCollection services)
    {
        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info = new OpenApiInfo
                {
                    Title = "Ressoa — API de Telemetria",
                    Version = "v1",
                    Description = """
                        Recebe o status diário dos aparelhos Ressoa (bateria, temperatura da água e pulso de ultrassom)
                        e informa ao app se a água de cada recipiente está protegida.

                        **Autenticação:** faça login na API Java (`POST /api/auth/login`), copie o `token` e use **Authorize**.
                        **Idempotência:** `POST /api/v1/leituras` exige o header `Idempotency-Key` (até 64 caracteres, ex.: um UUID).
                        **Rate limit:** excesso de requisições devolve `429` com `Retry-After`.
                        """
                };
                doc.Components ??= new OpenApiComponents();
                doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                doc.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Token JWT emitido pela API Java do Ressoa"
                };
                doc.Security ??= [];
                doc.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", doc)] = []
                });
                return Task.CompletedTask;
            });
        });
        return services;
    }

    public static WebApplication MapDocumentacao(this WebApplication app)
    {
        app.MapOpenApi("/openapi/{documentName}.json");
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/openapi/v1.json", "Ressoa Telemetria v1");
            o.RoutePrefix = "swagger";
            o.DocumentTitle = "Ressoa — API de Telemetria";
        });
        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
        return app;
    }
}
