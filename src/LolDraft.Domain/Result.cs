namespace LolDraft.Domain;

/// <summary>Resultado de uma operação de domínio, com mensagem pronta para mostrar ao usuário.</summary>
public readonly record struct Result(bool Ok, string? Error)
{
    public static Result Success() => new(true, null);
    public static Result Fail(string error) => new(false, error);
}

public readonly record struct Result<T>(bool Ok, T? Value, string? Error)
{
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
}
