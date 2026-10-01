using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class FaceProcessingStateConfiguration : IEntityTypeConfiguration<FaceProcessingState>
{
    public void Configure(EntityTypeBuilder<FaceProcessingState> builder)
    {
        builder.ToTable("FaceProcessingStates");
        builder.HasKey(x => x.ImageId);
        builder.Property(x => x.ImageFingerprint).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.ProcessedUtc).HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.Image).WithMany().HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FaceModel).WithMany().HasForeignKey(x => x.FaceModelId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.FaceModelId, x.Status });
    }
}