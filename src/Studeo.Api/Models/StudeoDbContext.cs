using Microsoft.EntityFrameworkCore;
using Studeo.Api.Contracts;

namespace Studeo.Api.Models;

public class StudeoDbContext(DbContextOptions<StudeoDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentPage> DocumentPages => Set<DocumentPage>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<StudySet> StudySets => Set<StudySet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Course>(e =>
        {
            e.Property(c => c.OwnerId).HasMaxLength(64);
            e.Property(c => c.Name).HasMaxLength(CourseLimits.MaxNameLength);
            e.HasIndex(c => new { c.OwnerId, c.Name });
        });

        modelBuilder.Entity<Document>(e =>
        {
            e.Property(d => d.FileName).HasMaxLength(260);
            e.Property(d => d.OwnerId).HasMaxLength(64);
            e.Property(d => d.ContentHash).HasMaxLength(64);
            e.HasIndex(d => new { d.OwnerId, d.UploadedAt });
            e.Property(d => d.TextHash).HasMaxLength(64);
            e.HasIndex(d => new { d.OwnerId, d.ContentHash });
            e.HasIndex(d => new { d.OwnerId, d.TextHash });
            // Deleting a course deletes its PDFs, and with them their pages, sections and quizzes.
            e.HasOne(d => d.Course).WithMany(c => c.Documents)
                .HasForeignKey(d => d.CourseId).OnDelete(DeleteBehavior.Cascade);
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
            e.Property(s => s.Name).HasMaxLength(StudySetLimits.MaxNameLength);
            e.Property(s => s.Content).HasColumnType("jsonb");
            e.HasOne(s => s.Section).WithMany(x => x.StudySets)
                .HasForeignKey(s => s.SectionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
