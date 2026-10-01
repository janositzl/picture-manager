using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class FaceModelConfiguration : IEntityTypeConfiguration<FaceModel>
{
    public void Configure(EntityTypeBuilder<FaceModel> builder)
    {
        builder.ToTable("FaceModels");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ModelHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.ModelHash).IsUnique();
    }
}