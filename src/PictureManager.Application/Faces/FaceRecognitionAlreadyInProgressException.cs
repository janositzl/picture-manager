using System;

namespace PictureManager.Application.Faces;

public sealed class FaceRecognitionAlreadyInProgressException()
    : Exception("Another job (scan, discovery or face recognition) is already in progress.");
