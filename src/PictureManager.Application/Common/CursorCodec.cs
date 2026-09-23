using System;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PictureManager.Application.Common;

/// <summary>Opaque keyset cursors: base64url(JSON). Clients never build or parse them.</summary>
public static class CursorCodec
{
    public static string Encode<T>(T payload) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));

    public static bool TryDecode<T>(string? cursor, [NotNullWhen(true)] out T? payload) where T : class
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(cursor))
            return false;

        try
        {
            payload = JsonSerializer.Deserialize<T>(Base64Url.DecodeFromChars(cursor));
            return payload is not null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or NotSupportedException)
        {
            payload = null;
            return false;
        }
    }
}
