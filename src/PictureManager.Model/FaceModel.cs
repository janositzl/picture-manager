using System;

namespace PictureManager.Model;

/// <summary>The model that produced a set of embeddings. Embeddings from different models are never compared.</summary>
public class FaceModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int EmbeddingDimensions { get; set; }

    /// <summary>SHA-256 (hex) of the model files; identifies the model across restarts.</summary>
    public string ModelHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}