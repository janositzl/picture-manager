using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public const int SystemUserId = 1;

    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("AppUsers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ZitadelSubjectId)
            .HasMaxLength(200);

        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(x => x.ZitadelSubjectId)
            .IsUnique();

        // v1 placeholder owner: every Album points at this single seeded system user until v2 auth lands.
        builder.HasData(new AppUser
        {
            Id = SystemUserId,
            DisplayName = "System",
            Role = UserRole.User,
            ZitadelSubjectId = null
        });
    }
}
