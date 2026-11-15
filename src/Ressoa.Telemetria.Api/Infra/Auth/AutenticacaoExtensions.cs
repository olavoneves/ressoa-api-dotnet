using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Ressoa.Telemetria.Api.Infra.Auth;

public sealed class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "ressoa-api";
    public string Audience { get; set; } = "ressoa";
}

public static class AutenticacaoExtensions
{
    public const int TamanhoMinimoSegredoBytes = 32;

    /// <summary>
    /// Valida o JWT HS256 emitido pela API Java: mesma chave (JWT_SECRET), emissor, audiência e expiração.
    /// O papel vem da claim "papel" (CIDADAO ou GESTOR).
    /// </summary>
    public static IServiceCollection AddAutenticacaoRessoa(this IServiceCollection services, IConfiguration config)
    {
        var opcoes = new JwtOptions
        {
            Secret = config["JWT_SECRET"] ?? config["Jwt:Secret"] ?? string.Empty,
            Issuer = config["JWT_ISSUER"] ?? config["Jwt:Issuer"] ?? "ressoa-api",
            Audience = config["JWT_AUDIENCE"] ?? config["Jwt:Audience"] ?? "ressoa"
        };
        if (Encoding.UTF8.GetByteCount(opcoes.Secret) < TamanhoMinimoSegredoBytes)
        {
            throw new InvalidOperationException(
                "JWT_SECRET ausente ou curto: defina a variável de ambiente com pelo menos 32 bytes (o mesmo da API Java).");
        }

        services.AddSingleton(opcoes);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = opcoes.Issuer,
                    ValidateAudience = true,
                    ValidAudience = opcoes.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opcoes.Secret)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtClaims.Email,
                    RoleClaimType = JwtClaims.Papel
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Politicas.Autenticado, p => p.RequireAuthenticatedUser())
            .AddPolicy(Politicas.Gestor, p => p.RequireRole(UsuarioAtual.PapelGestor));
        return services;
    }
}

public static class Politicas
{
    public const string Autenticado = "autenticado";
    public const string Gestor = "gestor";
}
