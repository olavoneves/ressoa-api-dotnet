namespace Ressoa.Telemetria.Api.Application;

/// <summary>Resultado de uma operação de aplicação, traduzido para HTTP nos endpoints.</summary>
public enum TipoFalha
{
    NaoEncontrado,
    Proibido,
    Invalido,
    ConflitoIdempotencia
}

public sealed record Falha(TipoFalha Tipo, string Mensagem);

public sealed class Resultado<T>
{
    public T? Valor { get; }
    public Falha? Falha { get; }

    /// <summary>true quando a resposta é a repetição de uma requisição já processada (idempotência).</summary>
    public bool Repetido { get; }

    public bool Sucesso => Falha is null;

    private Resultado(T? valor, Falha? falha, bool repetido)
    {
        Valor = valor;
        Falha = falha;
        Repetido = repetido;
    }

    public static Resultado<T> Ok(T valor, bool repetido = false) => new(valor, null, repetido);

    public static Resultado<T> Erro(TipoFalha tipo, string mensagem) => new(default, new Falha(tipo, mensagem), false);
}
