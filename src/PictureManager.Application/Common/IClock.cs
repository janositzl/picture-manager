using System;

namespace PictureManager.Application.Common;

public interface IClock
{
    DateTime UtcNow { get; }
}
