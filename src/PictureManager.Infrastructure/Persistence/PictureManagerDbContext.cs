using Microsoft.EntityFrameworkCore;

namespace PictureManager.Infrastructure.Persistence;

public sealed class PictureManagerDbContext : DbContext
{
    public PictureManagerDbContext(DbContextOptions<PictureManagerDbContext> options)
        : base(options)
    {
    }
}
