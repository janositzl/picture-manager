using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("Folders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.RelativePath)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.ModifiedUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.MissingSinceUtc)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.ChildrenDiscoveredAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.LastWriteTimeUtc)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.LastScannedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(x => x.ScanStatus);

        builder.HasOne(x => x.Root)
            .WithMany()
            .HasForeignKey(x => x.RootId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.RootId);
        builder.HasIndex(x => x.ParentId);
        builder.HasIndex(x => x.RelativePath);
    }
}
