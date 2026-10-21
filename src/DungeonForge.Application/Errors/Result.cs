namespace DungeonForge.Application.Errors;

// Handler outcome. A handler with no failure path may return its value directly instead.
public readonly struct Result
{
    private Result(Error? error) => Error = error;

    public static Result Success => new(null);

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => Error is not null;

    public static implicit operator Result(Error error) => new(error);

    public T Match<T>(Func<T> success, Func<Error, T> failure) => Error is { } error ? failure(error) : success();
}

public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => Error is not null;

    public T Value => Error is null
        ? _value!
        : throw new InvalidOperationException($"Result failed with {Error.Code}; there is no value.");

    public static implicit operator Result<T>(T value) => new(value, null);

    public static implicit operator Result<T>(Error error) => new(default, error);

    public TResult Match<TResult>(Func<T, TResult> success, Func<Error, TResult> failure) =>
        Error is { } error ? failure(error) : success(_value!);
}
