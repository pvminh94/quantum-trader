// ═══════════════════════════════════════════════════════════════════
// Result.cs - Functional error-handling pattern.
//
// Eliminates exceptions for expected failure cases (validation,
// business logic). Forces callers to handle both success and failure.
// Inspired by Rust's Result<T, E> pattern.
// ═══════════════════════════════════════════════════════════════════

namespace TradingEngine.Shared;

/// <summary>
/// Generic result type that represents either success (with value)
/// or failure (with error details).
/// </summary>
public sealed class Result<T>
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The success value. Access only when IsSuccess is true.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// The error details. Access only when IsFailure is true.
    /// </summary>
    public Error? Error { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
        Error = null;
    }

    private Result(Error error)
    {
        IsSuccess = false;
        Value = default;
        Error = error;
    }

    /// <summary>Create a success result.</summary>
    public static Result<T> Success(T value) => new(value);

    /// <summary>Create a failure result.</summary>
    public static Result<T> Failure(Error error) => new(error);

    /// <summary>
    /// Execute one of two functions depending on result state.
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
        => IsSuccess ? onSuccess(Value!) : onFailure(Error!);

    /// <summary>
    /// Chain another operation that may fail.
    /// </summary>
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> next)
        => IsSuccess ? next(Value!) : Result<TNext>.Failure(Error!);

    public void Deconstruct(out bool isSuccess, out T? value, out Error? error)
    {
        isSuccess = IsSuccess;
        value = Value;
        error = Error;
    }
}

/// <summary>
/// Non-generic result for void operations.
/// </summary>
public sealed class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    private Result()
    {
        IsSuccess = true;
        Error = null;
    }

    private Result(Error error)
    {
        IsSuccess = false;
        Error = error;
    }

    public static Result Success() => new();
    public static Result Failure(Error error) => new(error);

    public TResult Match<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
        => IsSuccess ? onSuccess() : onFailure(Error!);

    public static implicit operator Result(Error error) => new(error);
}

/// <summary>
/// Represents a domain error with a code, message, and optional details.
/// </summary>
public sealed record Error
{
    /// <summary>Machine-readable error code (e.g., "order.insufficient_funds").</summary>
    public string Code { get; }

    /// <summary>Human-readable error message.</summary>
    public string Message { get; }

    /// <summary>Optional detailed context (e.g., stack trace or field-level errors).</summary>
    public string? Detail { get; }

    public Error(string code, string message, string? detail = null)
    {
        Code = Guard.NotNullOrWhiteSpace(code, nameof(code));
        Message = Guard.NotNullOrWhiteSpace(message, nameof(message));
        Detail = detail;
    }

    // Common error factories
    public static Error NotFound(string entity, string id) =>
        new("not_found", $"{entity} with ID '{id}' was not found.");

    public static Error Validation(string field, string reason) =>
        new("validation_error", $"Field '{field}' is invalid: {reason}");

    public static Error Conflict(string description) =>
        new("conflict", description);

    public static Error Unauthorized(string reason = "Authentication required.") =>
        new("unauthorized", reason);

    public static Error ExchangeError(string details) =>
        new("exchange_error", $"Exchange returned an error: {details}");
}