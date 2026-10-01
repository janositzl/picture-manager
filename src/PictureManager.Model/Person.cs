using System;
using System.Collections.Generic;

namespace PictureManager.Model;

/// <summary>Name null = an unnamed group created by clustering.</summary>
public class Person
{
    public int Id { get; set; }
    public string? Name { get; set; }

    /// <summary>The face shown for this person. No FK on purpose (avoids a Faces<->People cycle); may be stale.</summary>
    public int? CoverFaceId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public ICollection<Face> Faces { get; set; } = new List<Face>();
}