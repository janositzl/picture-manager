using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("AppUsers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Username).IsRequired().HasMaxLength(64);
        builder.Property(x => x.NormalizedUsername).IsRequired().HasMaxLength(64);
        builder.HasIndex(x => x.NormalizedUsername).IsUnique();
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PasswordHash).HasMaxLength(500);
        // No HasDefaultValue(true) on IsActive: EF would treat an explicit `false` as "unset" on insert and
        // let the database default win, creating disabled users as active.

        // The pre-accounts "System" owner becomes the initial admin, keeping every existing album.
        // Its password is set at startup from Auth:InitialAdmin:Password (AdminBootstrapper).
        builder.HasData(new AppUser
        {
            Id = AppUser.InitialAdminId,
            Username = "admin",
            NormalizedUsername = "admin",
            DisplayName = "Administrator",
            Role = UserRole.Admin,
            IsActive = true,
            PasswordHash = null,
            MustChangePassword = false,
            CanRunFolderActions = false,
            SecurityStamp = new Guid("6f1d2c3b-4a59-4e6f-8a7b-9c0d1e2f3a4b"),
            CreatedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
