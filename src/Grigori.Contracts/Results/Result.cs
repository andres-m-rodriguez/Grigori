namespace Grigori.Contracts.Results;

/// <summary>
/// Represents a result that can either be a success with a value or a failure with an error.
/// Provides a monadic interface for composing operations that may fail.
/// </summary>
/// <typeparam name="T">The type of the success value</typeparam>
/// <typeparam name="TError">The type of the error (must be a record struct)</typeparam>
public readonly struct Result<T, TError>
    where TError : struct
{
    private readonly T? _value;
    private readonly TError? _error;
    private readonly bool _isSuccess;

    private Result(T value)
    {
        _value = value;
        _error = default;
        _isSuccess = true;
    }

    private Result(TError error)
    {
        _value = default;
        _error = error;
        _isSuccess = false;
    }

    /// <summary>
    /// Returns true if the result represents a success.
    /// </summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>
    /// Returns true if the result represents a failure.
    /// </summary>
    public bool IsFailure => !_isSuccess;

    /// <summary>
    /// Gets the success value. Throws if the result is a failure.
    /// </summary>
    public T Value => _isSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed result.");

    /// <summary>
    /// Gets the error value. Throws if the result is a success.
    /// </summary>
    public TError Error => !_isSuccess
        ? _error!.Value
        : throw new InvalidOperationException("Cannot access Error on a successful result.");

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result<T, TError> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static Result<T, TError> Failure(TError error) => new(error);

    /// <summary>
    /// Implicit conversion from success value.
    /// </summary>
    public static implicit operator Result<T, TError>(T value) => Success(value);

    /// <summary>
    /// Implicit conversion from error value.
    /// </summary>
    public static implicit operator Result<T, TError>(TError error) => Failure(error);

    /// <summary>
    /// Pattern matches on the result, executing the appropriate function.
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<TError, TResult> onFailure)
    {
        return _isSuccess ? onSuccess(_value!) : onFailure(_error!.Value);
    }

    /// <summary>
    /// Pattern matches on the result, executing the appropriate action.
    /// </summary>
    public void Switch(Action<T> onSuccess, Action<TError> onFailure)
    {
        if (_isSuccess)
            onSuccess(_value!);
        else
            onFailure(_error!.Value);
    }

    /// <summary>
    /// Maps the success value to a new value using the provided function.
    /// If the result is a failure, the error is propagated.
    /// </summary>
    public Result<TNew, TError> Map<TNew>(Func<T, TNew> mapper)
    {
        return _isSuccess
            ? Result<TNew, TError>.Success(mapper(_value!))
            : Result<TNew, TError>.Failure(_error!.Value);
    }

    /// <summary>
    /// Maps the error to a new error type using the provided function.
    /// If the result is a success, the value is propagated.
    /// </summary>
    public Result<T, TNewError> MapError<TNewError>(Func<TError, TNewError> mapper)
        where TNewError : struct
    {
        return _isSuccess
            ? Result<T, TNewError>.Success(_value!)
            : Result<T, TNewError>.Failure(mapper(_error!.Value));
    }

    /// <summary>
    /// Chains another operation that returns a Result.
    /// If this result is a failure, the error is propagated.
    /// </summary>
    public Result<TNew, TError> Bind<TNew>(Func<T, Result<TNew, TError>> binder)
    {
        return _isSuccess ? binder(_value!) : Result<TNew, TError>.Failure(_error!.Value);
    }

    /// <summary>
    /// Chains an async operation that returns a Result.
    /// If this result is a failure, the error is propagated.
    /// </summary>
    public async Task<Result<TNew, TError>> BindAsync<TNew>(Func<T, Task<Result<TNew, TError>>> binder)
    {
        return _isSuccess ? await binder(_value!) : Result<TNew, TError>.Failure(_error!.Value);
    }

    /// <summary>
    /// Returns the success value or a default value if the result is a failure.
    /// </summary>
    public T GetValueOrDefault(T defaultValue = default!)
    {
        return _isSuccess ? _value! : defaultValue;
    }

    /// <summary>
    /// Returns the success value or throws the error as an exception.
    /// </summary>
    public T GetValueOrThrow(Func<TError, Exception>? exceptionFactory = null)
    {
        if (_isSuccess)
            return _value!;

        if (exceptionFactory != null)
            throw exceptionFactory(_error!.Value);

        throw new InvalidOperationException($"Result failed with error: {_error}");
    }

    /// <summary>
    /// Executes an action if the result is successful.
    /// </summary>
    public Result<T, TError> OnSuccess(Action<T> action)
    {
        if (_isSuccess)
            action(_value!);
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure.
    /// </summary>
    public Result<T, TError> OnFailure(Action<TError> action)
    {
        if (!_isSuccess)
            action(_error!.Value);
        return this;
    }

    /// <summary>
    /// Gets the value or null if failed. Only works for reference types.
    /// </summary>
    public T? ValueOrNull => _isSuccess ? _value : default;

    public override string ToString()
    {
        return _isSuccess ? $"Success({_value})" : $"Failure({_error})";
    }
}

/// <summary>
/// Extension methods for Result types.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Converts a nullable value to a Result.
    /// </summary>
    public static Result<T, TError> ToResult<T, TError>(this T? value, TError errorIfNull)
        where T : class
        where TError : struct
    {
        return value is not null
            ? Result<T, TError>.Success(value)
            : Result<T, TError>.Failure(errorIfNull);
    }

    /// <summary>
    /// Converts a nullable struct to a Result.
    /// </summary>
    public static Result<T, TError> ToResult<T, TError>(this T? value, TError errorIfNull)
        where T : struct
        where TError : struct
    {
        return value.HasValue
            ? Result<T, TError>.Success(value.Value)
            : Result<T, TError>.Failure(errorIfNull);
    }

    /// <summary>
    /// Awaits a Task and wraps exceptions in a Result.
    /// </summary>
    public static async Task<Result<T, TError>> TryAsync<T, TError>(
        Func<Task<T>> operation,
        Func<Exception, TError> errorFactory)
        where TError : struct
    {
        try
        {
            var result = await operation();
            return Result<T, TError>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<T, TError>.Failure(errorFactory(ex));
        }
    }

    /// <summary>
    /// Combines multiple Results into a single Result containing all values.
    /// If any result fails, returns the first failure.
    /// </summary>
    public static Result<List<T>, TError> Combine<T, TError>(this IEnumerable<Result<T, TError>> results)
        where TError : struct
    {
        var values = new List<T>();
        foreach (var result in results)
        {
            if (result.IsFailure)
                return Result<List<T>, TError>.Failure(result.Error);
            values.Add(result.Value);
        }
        return Result<List<T>, TError>.Success(values);
    }
}
