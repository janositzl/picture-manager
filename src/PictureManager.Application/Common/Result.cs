using System;
using System.Collections.Generic;

namespace PictureManager.Application.Common;

public enum ResultStatus
{
    Success,
    NotFound,
    Invalid,
    Conflict,
    Forbidden
}

/// <summary>Outcome of a service operation that has no value. Endpoints map it to HTTP.</summary>
public sealed record Result(ResultStatus Status, IReadOnlyDictionary<string, string[]>? Errors = null, string? Message = null)
{
    public bool IsSuccess => Status == ResultStatus.Success;

    public static Result Ok() => new(ResultStatus.Success);

    public static Result NotFound() => new(ResultStatus.NotFound);

    public static Result Invalid(string field, string message) =>
        new(ResultStatus.Invalid, new Dictionary<string, string[]> { [field] = new[] { message } });

    public static Result Conflict(string message) => new(ResultStatus.Conflict, Message: message);

    /// <summary>The caller can see the resource but may not do this to it (HTTP 403).</summary>
    public static Result Forbidden(string message) => new(ResultStatus.Forbidden, Message: message);
}

/// <summary>Outcome of a service operation that returns a value on success.</summary>
public sealed record Result<T>(ResultStatus Status, T? Value, IReadOnlyDictionary<string, string[]>? Errors = null, string? Message = null)
{
    public bool IsSuccess => Status == ResultStatus.Success;

    public static Result<T> Ok(T value) => new(ResultStatus.Success, value);

    // Lets a method returning Result<T> write `return Result.NotFound();`.
    public static implicit operator Result<T>(Result failure) =>
        failure.IsSuccess
            ? throw new InvalidOperationException("Only a failed Result converts to Result<T>; use Result<T>.Ok(value).")
            : new Result<T>(failure.Status, default, failure.Errors, failure.Message);
}
