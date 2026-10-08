using Microsoft.EntityFrameworkCore;
using ODataAgentSample.Domain;

namespace ODataAgentSample.Infrastructure.Db;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WorldRegion> WorldRegions => Set<WorldRegion>();
    public DbSet<CountryRegion> CountryRegions => Set<CountryRegion>();
    public DbSet<Country> Countries => Set<Country>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorldRegion>(b =>
        {
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ISO).HasMaxLength(10);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.NameGER).HasMaxLength(200);
        });

        modelBuilder.Entity<CountryRegion>(b =>
        {
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ISO).HasMaxLength(10);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.NameGER).HasMaxLength(200);
            b.HasOne(x => x.WorldRegion).WithMany(w => w.CountryRegions)
                .HasForeignKey(x => x.WorldRegionId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Country>(b =>
        {
            // Ids kommen aus den Seed-Daten; neue Laender bekommen die naechste freie Id von SQLite
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.DisplayName).HasMaxLength(200);
            b.Property(x => x.DisplayNameGER).HasMaxLength(200);
            b.Property(x => x.EU).HasMaxLength(3);
            b.Property(x => x.EWR).HasMaxLength(3);
            b.Property(x => x.ISO2).HasMaxLength(10);
            b.Property(x => x.ISO3).HasMaxLength(10);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.NameGER).HasMaxLength(200);
            b.HasIndex(x => x.ISO2);
            b.HasOne(x => x.WorldRegion).WithMany(w => w.Countries)
                .HasForeignKey(x => x.WorldRegionId).OnDelete(DeleteBehavior.NoAction);
            b.HasOne(x => x.CountryRegion).WithMany(r => r.Countries)
                .HasForeignKey(x => x.CountryRegionId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
