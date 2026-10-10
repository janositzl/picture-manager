using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Common;

namespace PictureManager.Api.Endpoints;

/// <summary>Maps service Results onto TypedResults unions, so each handler's responses are in its signature.</summary>
public static class ResultHttpExtensions
{
    public static Results<Ok<T>, NotFound, ValidationProblem, Conflict<ProblemDetails>> ToOk<T>(this Result<T> result) =>
        result.Status switch
        {
            ResultStatus.Success => TypedResults.Ok(result.Value!),
            ResultStatus.NotFound => TypedResults.NotFound(),
            ResultStatus.Invalid => TypedResults.ValidationProblem(ToErrors(result.Errors)),
            ResultStatus.Conflict => TypedResults.Conflict(ConflictProblem(result.Message)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
        };

    public static Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>> ToNoContent(this Result result) =>
        result.Status switch
        {
            ResultStatus.Success => TypedResults.NoContent(),
            ResultStatus.NotFound => TypedResults.NotFound(),
            ResultStatus.Invalid => TypedResults.ValidationProblem(ToErrors(result.Errors)),
            ResultStatus.Conflict => TypedResults.Conflict(ConflictProblem(result.Message)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
        };

    /// <summary>ToOk for resources with access levels: Forbidden becomes a 403 ProblemDetails carrying the message.</summary>
    public static Results<Ok<T>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult> ToOkOrForbidden<T>(this Result<T> result) =>
        result.Status switch
        {
            ResultStatus.Success => TypedResults.Ok(result.Value!),
            ResultStatus.NotFound => TypedResults.NotFound(),
            ResultStatus.Invalid => TypedResults.ValidationProblem(ToErrors(result.Errors)),
            ResultStatus.Conflict => TypedResults.Conflict(ConflictProblem(result.Message)),
            ResultStatus.Forbidden => ForbiddenProblem(result.Message),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
        };

    public static Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult> ToNoContentOrForbidden(this Result result) =>
        result.Status switch
        {
            ResultStatus.Success => TypedResults.NoContent(),
            ResultStatus.NotFound => TypedResults.NotFound(),
            ResultStatus.Invalid => TypedResults.ValidationProblem(ToErrors(result.Errors)),
            ResultStatus.Conflict => TypedResults.Conflict(ConflictProblem(result.Message)),
            ResultStatus.Forbidden => ForbiddenProblem(result.Message),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
        };

    internal static ProblemHttpResult ForbiddenProblem(string? message) =>
        TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: message);

    public static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = new[] { message } });

    internal static Dictionary<string, string[]> ToErrors(IReadOnlyDictionary<string, string[]>? errors) =>
        errors is null ? new Dictionary<string, string[]>() : new Dictionary<string, string[]>(errors);

    internal static ProblemDetails ConflictProblem(string? message) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Conflict",
        Detail = message
    };
}
