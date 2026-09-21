using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class AlbumImageConfiguration : IEntityTypeConfiguration<AlbumImage>
{
    public void Configure(EntityTypeBuilder<AlbumImage> builder)
    {
        builder.ToTable("AlbumImages");

        builder.HasKey(x => new { x.AlbumId, x.ImageId });

        builder.Property(x => x.AddedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(x => x.Album)
            .WithMany(x => x.AlbumImages)
            .HasForeignKey(x => x.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Image)
            .WithMany(x => x.AlbumImages)
            .HasForeignKey(x => x.ImageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ImageId);
    }
}
