
using AxialFanMVC.Database;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Database
{
    public class AxialFanDbContext : DbContext
    {
        public AxialFanDbContext(DbContextOptions<AxialFanDbContext> options) : base(options) { }

        //Exception log table
        public DbSet<ExceptionLogEntry> exception_logs => Set<ExceptionLogEntry>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Project> Projects => Set<Project>();
        public DbSet<BladeProfile> blade_profiles => Set<BladeProfile>();
        public DbSet<DesignInput> design_inputs => Set<DesignInput>();
        public DbSet<DesignResult> design_results => Set<DesignResult>();
        public DbSet<PerformanceCurve> performance_curves => Set<PerformanceCurve>();
        public DbSet<Drawing> drawings => Set<Drawing>();
        public DbSet<ExportLog> export_logs => Set<ExportLog>();
        public DbSet<DesignSeries> design_series => Set<DesignSeries>();
        public DbSet<HandbookChunk> handbook_chunks => Set<HandbookChunk>();
        public DbSet<CalibrationCase> calibration_cases => Set<CalibrationCase>();
        public DbSet<CalibrationCasePoint> calibration_case_points => Set<CalibrationCasePoint>();

        public DbSet<CostRate> cost_rates => Set<CostRate>();
        public DbSet<BomLineItem> bom_line_items => Set<BomLineItem>();
        public ICollection<BomLineItem> BomLineItems { get; set; } = new List<BomLineItem>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);

            // ── ExceptionLogs ───────────────────────────────────────
            mb.Entity<ExceptionLogEntry>(e =>
            {
                e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // ── Users ──────────────────────────────────────────────
            mb.Entity<User>(e =>
            {
                e.HasIndex(u => u.Email).IsUnique();
                e.Property(u => u.Role).HasDefaultValue("user");
                e.Property(u => u.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(u => u.UpdatedAt)
                 .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");
            });

            // ── Projects ───────────────────────────────────────────
            mb.Entity<Project>(e =>
            {
                e.Property(p => p.Status).HasDefaultValue("draft");
                e.Property(p => p.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(p => p.UpdatedAt)
                 .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");
                e.HasOne(p => p.User)
                 .WithMany(u => u.Projects)
                 .HasForeignKey(p => p.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── BladeProfiles ──────────────────────────────────────
            mb.Entity<BladeProfile>(e =>
            {
                e.HasIndex(b => b.Name).IsUnique();
                e.Property(b => b.Type).HasDefaultValue("NACA");
                e.HasData(
                    new BladeProfile { Id = 1, Name = "NACA 4412", Type = "NACA", Description = "Cambered, general purpose — recommended default" },
                    new BladeProfile { Id = 2, Name = "NACA 2412", Type = "NACA", Description = "Low camber, low-pressure applications" },
                    new BladeProfile { Id = 3, Name = "NACA 0012", Type = "NACA", Description = "Symmetric, reversible flow" },
                    new BladeProfile { Id = 4, Name = "Flat plate", Type = "custom", Description = "Simplified flat blade geometry" }
                );
            });

            // ── DesignInputs ───────────────────────────────────────
            mb.Entity<DesignInput>(e =>
            {
                e.Property(d => d.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasOne(d => d.Project)
                 .WithMany(p => p.DesignInputs)
                 .HasForeignKey(d => d.ProjectId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(d => d.BladeProfile)
                 .WithMany(b => b.DesignInputs)
                 .HasForeignKey(d => d.BladeProfileId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // ── DesignResults ──────────────────────────────────────
            mb.Entity<DesignResult>(e =>
            {
                e.Property(d => d.Status).HasDefaultValue("ok");
                e.Property(d => d.CalculatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasOne(d => d.DesignInput)
                 .WithOne(di => di.DesignResult)
                 .HasForeignKey<DesignResult>(d => d.DesignInputId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── PerformanceCurves ──────────────────────────────────
            mb.Entity<PerformanceCurve>(e =>
            {
                e.Property(p => p.GeneratedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(p => p.QValues).HasColumnType("mediumtext");
                e.Property(p => p.DpValues).HasColumnType("mediumtext");
                e.Property(p => p.EtaValues).HasColumnType("mediumtext");
                e.Property(p => p.KwValues).HasColumnType("mediumtext");
                e.HasOne(p => p.DesignResult)
                 .WithMany(r => r.PerformanceCurves)
                 .HasForeignKey(p => p.DesignResultId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── Drawings ───────────────────────────────────────────
            mb.Entity<Drawing>(e =>
            {
                e.Property(d => d.GeneratedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(d => d.SvgData).HasColumnType("longtext");
                e.HasOne(d => d.DesignResult)
                 .WithMany(r => r.Drawings)
                 .HasForeignKey(d => d.DesignResultId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── DesignSeries ───────────────────────────────────────
            mb.Entity<DesignSeries>(e =>
            {
                e.Property(s => s.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasOne(s => s.Project)
                 .WithMany()
                 .HasForeignKey(s => s.ProjectId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(s => s.BaseDesignInput)
                 .WithMany()
                 .HasForeignKey(s => s.BaseDesignInputId)
                 .OnDelete(DeleteBehavior.Restrict); // don't cascade-delete a series if its base design is deleted
                e.HasMany(s => s.Variants)
                 .WithOne(d => d.DesignSeries)
                 .HasForeignKey(d => d.DesignSeriesId)
                 .OnDelete(DeleteBehavior.SetNull); // deleting a series doesn't delete the variant designs
            });


                // ── ExportLogs ─────────────────────────────────────────
                mb.Entity<ExportLog>(e =>
            {
                e.Property(x => x.ExportedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasOne(x => x.Project)
                 .WithMany(p => p.ExportLogs)
                 .HasForeignKey(x => x.ProjectId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.User)
                 .WithMany(u => u.ExportLogs)
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── HandbookChunks ─────────────────────────────────────
          
            mb.Entity<HandbookChunk>(e =>
            {
                e.HasIndex(h => h.ChunkKey).IsUnique();
                e.Property(h => h.Text).HasColumnType("mediumtext");
                e.Property(h => h.Embedding).HasColumnType("mediumtext");
                e.Property(h => h.QualityFlag).HasDefaultValue("clean");
                e.Property(h => h.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // ── CalibrationCases ───────────────────────────────────
            mb.Entity<CalibrationCase>(e =>
            {
                e.Property(c => c.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasMany(c => c.Points)
                 .WithOne(p => p.CalibrationCase)
                 .HasForeignKey(p => p.CalibrationCaseId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<PerformanceCurve>(e =>
            {
               

                e.Property(p => p.ValidationStatus)
                    .HasDefaultValue("not_applicable");

                e.Property(p => p.ValidationFlagsJson)
                    .HasColumnType("mediumtext");

                e.HasOne(p => p.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(p => p.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
            // ── CostRates ────────────────────────────────────────
            mb.Entity<CostRate>(e =>
            {
                e.HasIndex(c => new { c.Category, c.RateKey }).IsUnique();
                e.Property(c => c.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // ── BomLineItems ─────────────────────────────────────
            mb.Entity<BomLineItem>(e =>
            {
                e.Property(b => b.Source).HasDefaultValue("Auto");
                e.Property(b => b.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasOne(b => b.DesignResult)
                 .WithMany(r => r.BomLineItems)
                 .HasForeignKey(b => b.DesignResultId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(b => b.CreatedByUser)
                 .WithMany()
                 .HasForeignKey(b => b.CreatedByUserId)
                 .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
