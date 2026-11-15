using System.Security.Claims;

namespace Ressoa.Telemetria.Api.Infra.Auth;

/// <summary>Usuário extraído do JWT emitido pela API Java (claims combinadas no ADR-004).</summary>
public sealed record UsuarioAtual(long Id, string? Email, string Papel)
{
    public const string PapelGestor = "GESTOR";
    public const string PapelCidadao = "CIDADAO";

    public bool EhGestor => Papel == PapelGestor;

    public static UsuarioAtual De(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtClaims.Sujeito)
                  ?? throw new InvalidOperationException("Token sem claim 'sub'.");
        return new UsuarioAtual(long.Parse(sub), principal.FindFirstValue(JwtClaims.Email),
            principal.FindFirstValue(JwtClaims.Papel) ?? PapelCidadao);
    }
}

public static class JwtClaims
{
    public const string Sujeito = "sub";
    public const string Email = "email";
    public const string Papel = "papel";
}
