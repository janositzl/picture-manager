using System.Collections.Generic;

namespace PictureManager.Application.Common;

/// <summary>One keyset page. NextCursor is null on the last page.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);
