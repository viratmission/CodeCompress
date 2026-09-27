using Microsoft.EntityFrameworkCore;
using CodeCompass.Api.Models;

namespace CodeCompass.Api.Data;

public class CodeCompassDbContext : DbContext
{
    public CodeCompassDbContext(DbContextOptions<CodeCompassDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<RepositoryFile> RepositoryFiles => Set<RepositoryFile>();
    public DbSet<RepositoryModule> RepositoryModules => Set<RepositoryModule>();
    public DbSet<RepositoryDependency> RepositoryDependencies => Set<RepositoryDependency>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<OnboardingPath> OnboardingPaths => Set<OnboardingPath>();
    public DbSet<OnboardingStep> OnboardingSteps => Set<OnboardingStep>();
    public DbSet<UserOnboarding> UserOnboardings => Set<UserOnboarding>();
    public DbSet<UserOnboardingStep> UserOnboardingSteps => Set<UserOnboardingStep>();
    public DbSet<StarterTask> StarterTasks => Set<StarterTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Repository>(entity =>
        {
            entity.HasIndex(r => r.Name);
        });

        modelBuilder.Entity<RepositoryFile>(entity =>
        {
            entity.HasIndex(f => new { f.RepositoryId, f.FilePath }).IsUnique();

            entity.HasOne(f => f.Repository)
                  .WithMany(r => r.Files)
                  .HasForeignKey(f => f.RepositoryId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RepositoryModule>(entity =>
        {
            entity.HasIndex(m => new { m.RepositoryId, m.Path });

            entity.HasOne(m => m.Repository)
                  .WithMany(r => r.Modules)
                  .HasForeignKey(m => m.RepositoryId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RepositoryDependency>(entity =>
        {
            entity.HasOne(d => d.Repository)
                  .WithMany(r => r.Dependencies)
                  .HasForeignKey(d => d.RepositoryId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Use NoAction to avoid multiple cascade paths
            entity.HasOne(d => d.SourceFile)
                  .WithMany()
                  .HasForeignKey(d => d.SourceFileId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.TargetFile)
                  .WithMany()
                  .HasForeignKey(d => d.TargetFileId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(d => new { d.SourceFileId, d.TargetFileId });
        });

        modelBuilder.Entity<OnboardingPath>(entity =>
        {
            entity.HasIndex(p => new { p.RepositoryId, p.Role });

            entity.HasOne(p => p.Repository)
                  .WithMany(r => r.OnboardingPaths)
                  .HasForeignKey(p => p.RepositoryId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OnboardingStep>(entity =>
        {
            entity.HasOne(s => s.OnboardingPath)
                  .WithMany(p => p.Steps)
                  .HasForeignKey(s => s.OnboardingPathId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(s => s.Module)
                  .WithMany()
                  .HasForeignKey(s => s.ModuleId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<UserOnboarding>(entity =>
        {
            entity.HasOne(u => u.Repository)
                  .WithMany()
                  .HasForeignKey(u => u.RepositoryId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(u => u.OnboardingPath)
                  .WithMany()
                  .HasForeignKey(u => u.OnboardingPathId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(u => u.User)
                  .WithMany()
                  .HasForeignKey(u => u.UserId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<UserOnboardingStep>(entity =>
        {
            entity.HasOne(s => s.UserOnboarding)
                  .WithMany(u => u.UserSteps)
                  .HasForeignKey(s => s.UserOnboardingId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(s => s.OnboardingStep)
                  .WithMany()
                  .HasForeignKey(s => s.OnboardingStepId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<StarterTask>(entity =>
        {
            entity.HasOne(t => t.Repository)
                  .WithMany(r => r.StarterTasks)
                  .HasForeignKey(t => t.RepositoryId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.RelatedModule)
                  .WithMany()
                  .HasForeignKey(t => t.RelatedModuleId)
                  .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
