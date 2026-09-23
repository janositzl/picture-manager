using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class ImageRootConfiguration : IEntityTypeConfiguration<ImageRoot>
{
    public void Configure(EntityTypeBuilder<ImageRoot> builder)
    {
        builder.ToTable("ImageRoots");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.MountPath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.Alias)
            .HasMaxLength(200);

        builder.Property(x => x.CreatedUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}
