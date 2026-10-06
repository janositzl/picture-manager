using System;
using System.Linq;
using System.Threading.Tasks;
using Pgvector;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Model;

namespace PictureManager.Tests.Support;

/// <summary>Seeds roots, folders, images, models and faces for face-related Postgres tests.</summary>
public static class FaceTestData
{
    public static async Task<Folder> SeedRootAsync(PictureManagerDbContext db, string mountPath = "/images")
    {
        var root = new ImageRoot { Name = "nas", MountPath = mountPath, IsActive = true, CreatedUtc = DateTime.UtcNow };
        var top = new Folder { Root = root, Name = "nas", RelativePath = string.Empty, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        db.Folders.Add(top);
        await db.SaveChangesAsync();
        return top;
    }

    public static async Task<Folder> AddFolderAsync(PictureManagerDbContext db, Folder parent, string name)
    {
        var folder = new Folder
        {
            RootId = parent.RootId,
            ParentId = parent.Id,
            Name = name,
            RelativePath = string.IsNullOrEmpty(parent.RelativePath) ? name : $"{parent.RelativePath}/{name}",
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder;
    }

    public static async Task<Image> AddImageAsync(
        PictureManagerDbContext db, Folder folder, string fileName, IndexState state = IndexState.Indexed, string hash = "hash")
    {
        var image = new Image
        {
            FolderId = folder.Id,
            FileName = fileName,
            Extension = ".jpg",
            ContentHash = hash,
            FileSize = 100,
            FileModified = DateTime.UtcNow,
            FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IndexState = state
        };
        db.Images.Add(image);
        await db.SaveChangesAsync();
        return image;
    }

    public static async Task<int> AddModelAsync(PictureManagerDbContext db, string hash = "model-1")
    {
        var model = new FaceModel { Name = "test", Version = "1", EmbeddingDimensions = 512, ModelHash = hash, CreatedUtc = DateTime.UtcNow };
        db.FaceModels.Add(model);
        await db.SaveChangesAsync();
        return model.Id;
    }

    public static async Task<Face> AddFaceAsync(
        PictureManagerDbContext db, int imageId, int modelId, float[] embedding,
        int? personId = null, FaceAssignmentState state = FaceAssignmentState.Unknown, float quality = 0.9f)
    {
        var face = new Face
        {
            ImageId = imageId,
            FaceModelId = modelId,
            PersonId = personId,
            AssignmentState = state,
            X = 0.1f, Y = 0.1f, Width = 0.2f, Height = 0.2f,
            DetectionConfidence = 0.9f,
            QualityScore = quality,
            Embedding = new Vector(embedding),
            CreatedUtc = DateTime.UtcNow
        };
        db.Faces.Add(face);
        await db.SaveChangesAsync();
        return face;
    }

    /// <summary>A unit vector along `axis`, rotated slightly toward axis+1 by `tilt` (radians): small tilt = near-duplicate.</summary>
    public static float[] Embedding(int axis, double tilt = 0)
    {
        var values = new float[512];
        values[axis] = (float)Math.Cos(tilt);
        values[(axis + 1) % 512] = (float)Math.Sin(tilt);
        return values;
    }

    public static float[] Normalize(float[] values)
    {
        var norm = (float)Math.Sqrt(values.Sum(v => v * v));
        return values.Select(v => v / norm).ToArray();
    }
}