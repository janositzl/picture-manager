using System;

namespace PictureManager.Application.Discovery;

public sealed class DiscoveryAlreadyInProgressException : Exception
{
    public DiscoveryAlreadyInProgressException() : base("A scan or discovery is already in progress.")
    {
    }
}
