using System.Globalization;
using System.Numerics;

namespace PictureManager.Application.Duplicates;

public static class PerceptualHash
{
    public static ulong? Parse(string? s) =>
        s is { Length: 16 } && ulong.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v) ? v : null;

    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>Flat/blank images hash to ~all-0 or ~all-1 bits and would all match each other.</summary>
    public static bool IsDegenerate(ulong h) => BitOperations.PopCount(h) is <= 2 or >= 62;
}
