using Microsoft.EntityFrameworkCore;

namespace Studeo.Api.Models;

public class StudeoDbContext(DbContextOptions<StudeoDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentPage> DocumentPages => Set<DocumentPage>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<StudySet> StudySets => Set<StudySet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(e =>
        {
            e.Property(d => d.FileName).HasMaxLength(260);
        });

        modelBuilder.Entity<DocumentPage>(e =>
        {
            e.HasOne(p => p.Document).WithMany(d => d.Pages)
                .HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.DocumentId, p.PageNumber }).IsUnique();
        });

        modelBuilder.Entity<Section>(e =>
        {
            e.Property(s => s.Title).HasMaxLength(500);
            e.HasOne(s => s.Document).WithMany(d => d.Sections)
                .HasForeignKey(s => s.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.DocumentId, s.Order }).IsUnique();
        });

        modelBuilder.Entity<StudySet>(e =>
        {
            e.Property(s => s.Difficulty).HasMaxLength(10);
            e.Property(s => s.Content).HasColumnType("jsonb");
            e.HasOne(s => s.Section).WithMany(x => x.StudySets)
                .HasForeignKey(s => s.SectionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
