using System;

namespace PictureManager.Application.Faces;

/// <summary>The face models are missing or can't be loaded: the whole face job fails, no per-image retries.</summary>
public sealed class FaceModelUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
