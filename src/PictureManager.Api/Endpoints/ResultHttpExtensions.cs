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

    internal static Dictionary<string, string[]> ToErrors(IReadOnlyDictionary<string, string[]>? errors) =>
        errors is null ? new Dictionary<string, string[]>() : new Dictionary<string, string[]>(errors);

    internal static ProblemDetails ConflictProblem(string? message) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Conflict",
        Detail = message
    };
}
