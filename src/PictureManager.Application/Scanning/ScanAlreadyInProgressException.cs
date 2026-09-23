using System;

namespace PictureManager.Application.Scanning;

public sealed class ScanAlreadyInProgressException : Exception
{
    public ScanAlreadyInProgressException() : base("A scan is already in progress.")
    {
    }
}
