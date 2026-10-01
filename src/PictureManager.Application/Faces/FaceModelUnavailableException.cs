using System;

namespace PictureManager.Application.Faces;

public sealed class FaceModelUnavailableException(string message) : Exception(message);
