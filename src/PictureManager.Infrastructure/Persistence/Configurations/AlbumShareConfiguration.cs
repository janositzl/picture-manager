using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class AlbumShareConfiguration : IEntityTypeConfiguration<AlbumShare>
{
    public void Configure(EntityTypeBuilder<AlbumShare> builder)
    {
        builder.ToTable("AlbumShares");

        builder.HasKey(x => new { x.AlbumId, x.UserId });

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(x => x.Album)
            .WithMany(x => x.Shares)
            .HasForeignKey(x => x.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // "Albums shared with me".
        builder.HasIndex(x => x.UserId);
    }
}
