using Microsoft.EntityFrameworkCore;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence;

public sealed class PictureManagerDbContext : DbContext
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
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<ScanJob> ScanJobs => Set<ScanJob>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PictureManagerDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
