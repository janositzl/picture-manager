using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence;

public sealed class PictureManagerDbContext : DbContext, IDataProtectionKeyContext
{
    public PictureManagerDbContext(DbContextOptions<PictureManagerDbContext> options)
        : base(options)
    {
    }

    public DbSet<ImageRoot> ImageRoots => Set<ImageRoot>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<AlbumImage> AlbumImages => Set<AlbumImage>();
    public DbSet<UserFavorite> UserFavorites => Set<UserFavorite>();
    public DbSet<AlbumShare> AlbumShares => Set<AlbumShare>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<FaceModel> FaceModels => Set<FaceModel>();
    public DbSet<FaceProcessingState> FaceProcessingStates => Set<FaceProcessingState>();
    public DbSet<Face> Faces => Set<Face>();
    public DbSet<Person> People => Set<Person>();

    /// <summary>ASP.NET Core Data Protection keys (they encrypt the auth cookie); kept here so restarts don't log everyone out.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PictureManagerDbContext).Assembly);

        // Non-Npgsql providers (EF InMemory in unit tests) cannot map Vector; store it as its string form there (float[] is treated as a primitive collection).
        if (!Database.IsNpgsql())
            modelBuilder.Entity<Face>().Property(x => x.Embedding)
                .HasConversion(v => v.ToString(), s => new Vector(s));

        base.OnModelCreating(modelBuilder);
    }
}
