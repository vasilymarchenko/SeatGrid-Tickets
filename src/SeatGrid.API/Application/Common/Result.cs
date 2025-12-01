namespace SeatGrid.API.Application.Common;

/// <summary>
/// Represents the result of an operation that can either succeed with a value or fail with an error.
/// </summary>
public class Result<TSuccess, TError>
{
    private readonly TSuccess? _success;
    private readonly TError? _error;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    private Result(TSuccess success)
    {
        _success = success;
        _error = default;
        IsSuccess = true;
    }

    private Result(TError error)
    {
        _success = default;
        _error = error;
        IsSuccess = false;
    }

    public static Result<TSuccess, TError> Success(TSuccess value) => new(value);
    public static Result<TSuccess, TError> Failure(TError error) => new(error);

    public TResult Match<TResult>(
        Func<TSuccess, TResult> onSuccess,
        Func<TError, TResult> onFailure)
    {
        return IsSuccess
            ? onSuccess(_success!)
            : onFailure(_error!);
    }

    public void Match(
        Action<TSuccess> onSuccess,
        Action<TError> onFailure)
    {
        if (IsSuccess)
            onSuccess(_success!);
        else
            onFailure(_error!);
    }

    public TSuccess GetSuccessOrThrow() =>
        IsSuccess ? _success! : throw new InvalidOperationException("Cannot get success value from a failed result.");

    public TError GetErrorOrThrow() =>
        IsFailure ? _error! : throw new InvalidOperationException("Cannot get error value from a successful result.");
}
