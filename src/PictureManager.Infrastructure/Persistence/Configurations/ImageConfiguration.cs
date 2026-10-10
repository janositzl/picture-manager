using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class ImageConfiguration : IEntityTypeConfiguration<Image>
{
    public void Configure(EntityTypeBuilder<Image> builder)
    {
        builder.ToTable("Images");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Extension)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.ContentHash)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.PerceptualHash)
            .HasMaxLength(200);

        builder.Property(x => x.CameraMake)
            .HasMaxLength(200);

        builder.Property(x => x.CameraModel)
            .HasMaxLength(200);

        builder.Property(x => x.LensModel)
            .HasMaxLength(200);

        builder.Property(x => x.RawMetadata)
            .HasColumnType("jsonb");

        // EXIF DateTimeOriginal has no timezone - store as local-naive, not a UTC instant.
        builder.Property(x => x.DateTaken)
            .HasColumnType("timestamp without time zone");

        // Stored generated column. "DateTaken" is local-naive; AT TIME ZONE 'UTC' reads its wall
        // clock as UTC so it can be COALESCEd with the timestamptz "FileModified" (immutable, as
        // generated columns require). Undated images therefore sort by file mtime.
        builder.Property(x => x.SortDate)
            .HasColumnType("timestamp with time zone")
            .HasComputedColumnSql("COALESCE(\"DateTaken\" AT TIME ZONE 'UTC', \"FileModified\")", stored: true);

        builder.Property(x => x.FileModified)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.FirstSeenUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.MissingSinceUtc)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(x => x.Folder)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.FolderId);
        builder.HasIndex(x => x.ContentHash);

        builder.Property(x => x.IsHidden).HasDefaultValue(false);
        builder.Property(x => x.ThumbnailRotation).HasDefaultValue(0);

        // Folder grid, date sort. The lower(FileName) index is an expression index
        // that EF's fluent API can't express; it is raw SQL in the Phase5RestApi migration.
        builder.HasIndex(x => new { x.FolderId, x.SortDate, x.Id });
    }
}
