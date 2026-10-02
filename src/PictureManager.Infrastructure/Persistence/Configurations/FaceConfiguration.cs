using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class FaceConfiguration : IEntityTypeConfiguration<Face>
{
    public void Configure(EntityTypeBuilder<Face> builder)
    {
        builder.ToTable("Faces");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Embedding).HasColumnType("vector(512)").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ClusteredUtc).HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.Image).WithMany().HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FaceModel).WithMany().HasForeignKey(x => x.FaceModelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Person).WithMany(p => p.Faces).HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ImageId);
        builder.HasIndex(x => x.PersonId);
        builder.HasIndex(x => new { x.FaceModelId, x.AssignmentState });
        builder.HasIndex(x => new { x.FaceModelId, x.ClusteredUtc });
        builder.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
    }
}