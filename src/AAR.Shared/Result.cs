// =============================================================================
// AAR.Shared - Result.cs
// Generic Result pattern for operation outcomes following the Railway-Oriented Programming pattern
// =============================================================================

namespace AAR.Shared;

/// <summary>
/// Represents the outcome of an operation that can succeed or fail.
/// Provides a monadic pattern for error handling without exceptions.
/// </summary>
/// <typeparam name="T">The type of the success value</typeparam>
public class Result<T>
{
    /// <summary>
    /// The success value (if successful)
    /// </summary>
    public T? Value { get; }
    
    /// <summary>
    /// The error (if failed)
    /// </summary>
    public Error? Error { get; }
    
    /// <summary>
    /// Indicates whether the operation succeeded
    /// </summary>
    public bool IsSuccess => Error is null;
    
    /// <summary>
    /// Indicates whether the operation failed
    /// </summary>
    public bool IsFailure => !IsSuccess;

    private Result(T? value, Error? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Creates a successful result with a value
    /// </summary>
    public static Result<T> Success(T value) => new(value, null);
    
    /// <summary>
    /// Creates a failed result with an error
    /// </summary>
    public static Result<T> Failure(Error error) => new(default, error);
    
    /// <summary>
    /// Creates a failed result from an error code and message
    /// </summary>
    public static Result<T> Failure(string code, string message) => 
        new(default, new Error(code, message));

    /// <summary>
    /// Implicit conversion from value to success result
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);
    
    /// <summary>
    /// Implicit conversion from error to failure result
    /// </summary>
    public static implicit operator Result<T>(Error error) => Failure(error);

    /// <summary>
    /// Maps the success value to a new type
    /// </summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> mapper)
    {
        return IsSuccess && Value is not null
            ? Result<TOut>.Success(mapper(Value))
            : Result<TOut>.Failure(Error!);
    }

    /// <summary>
    /// Binds to another result-producing operation
    /// </summary>
    public async Task<Result<TOut>> BindAsync<TOut>(Func<T, Task<Result<TOut>>> binder)
    {
        return IsSuccess && Value is not null
            ? await binder(Value)
            : Result<TOut>.Failure(Error!);
    }

    /// <summary>
    /// Pattern match on the result
    /// </summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        return IsSuccess && Value is not null
            ? onSuccess(Value)
            : onFailure(Error!);
    }
}

/// <summary>
/// Non-generic Result for operations that don't return a value
/// </summary>
public class Result
{
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    private Result(Error? error)
    {
        Error = error;
    }

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);
    public static Result Failure(string code, string message) => new(new Error(code, message));
    
    public static implicit operator Result(Error error) => Failure(error);
}
