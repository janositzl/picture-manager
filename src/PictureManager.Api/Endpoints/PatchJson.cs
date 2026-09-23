using System;
using System.Text.Json;

namespace PictureManager.Api.Endpoints;

/// <summary>
/// PATCH bodies need "absent" vs "explicit null" (e.g. `alias: null` clears the alias), which normal
/// record binding can't express. Each reader returns false only when the property has the wrong JSON type.
/// </summary>
public static class PatchJson
{
    public static bool TryReadString(JsonElement body, string property, out bool present, out string? value)
    {
        present = false;
        value = null;
        if (!TryFind(body, property, out var element))
            return true;

        present = true;
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return true;
            case JsonValueKind.String:
                value = element.GetString();
                return true;
            default:
                return false;
        }
    }

    public static bool TryReadBool(JsonElement body, string property, out bool present, out bool? value)
    {
        present = false;
        value = null;
        if (!TryFind(body, property, out var element))
            return true;

        present = true;
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static bool TryFind(JsonElement body, string property, out JsonElement element)
    {
        element = default;
        if (body.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var candidate in body.EnumerateObject())
        {
            if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                element = candidate.Value;
                return true;
            }
        }

        return false;
    }
}
