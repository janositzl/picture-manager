using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class ScanJobConfiguration : IEntityTypeConfiguration<ScanJob>
{
    public void Configure(EntityTypeBuilder<ScanJob> builder)
    {
        builder.ToTable("ScanJobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(4000);

        builder.Property(x => x.StartedUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.CompletedUtc)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.RootFolder)
            .WithMany()
            .HasForeignKey(x => x.RootFolderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Status);
    }
}
