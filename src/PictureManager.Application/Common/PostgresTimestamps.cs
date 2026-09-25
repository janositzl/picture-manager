using System;

namespace PictureManager.Application.Common;

public static class PostgresTimestamps
{
    // Postgres timestamptz stores microsecond precision; file-system times carry 100ns ticks. Truncate before
    // storing or comparing, so a round-tripped value compares equal to itself next time.
    public static DateTime TruncateToMicroseconds(DateTime value) => new(value.Ticks - (value.Ticks % 10), value.Kind);
}
