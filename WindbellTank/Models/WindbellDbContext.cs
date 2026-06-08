using Microsoft.EntityFrameworkCore;

namespace WindbellTank.Models
{
    /// <summary>
    /// Entity Framework Core DbContext — ofisServer bazasında WindbellTank cədvəllərini idarə edir.
    /// </summary>
    public class WindbellDbContext : DbContext
    {
        public WindbellDbContext(DbContextOptions<WindbellDbContext> options) : base(options) { }

        public DbSet<TankSetting>       TankSettings       { get; set; } = null!;
        public DbSet<ProbeSetting>      ProbeSettings      { get; set; } = null!;
        public DbSet<SensorSetting>     SensorSettings     { get; set; } = null!;
        public DbSet<OilProductSetting> OilProductSettings { get; set; } = null!;
        public DbSet<DensitySetting>    DensitySettings    { get; set; } = null!;
        public DbSet<GasSensorSetting>  GasSensorSettings  { get; set; } = null!;
        public DbSet<TankTableEntry>    TankTableEntries   { get; set; } = null!;
        public DbSet<SystemVersion>     SystemVersions     { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── TankSetting ──────────────────────────────────
            modelBuilder.Entity<TankSetting>(e =>
            {
                e.ToTable("WindbellTankSettings");
                e.HasKey(t => t.TankNo);
                e.Property(t => t.TankNo).HasMaxLength(10);
                e.Property(t => t.OilCode).HasMaxLength(20);
                e.Property(t => t.OilName).HasMaxLength(50);
                e.Property(t => t.ExpansionRate).HasMaxLength(20);
                e.Property(t => t.Version).HasMaxLength(10);
            });

            // ── ProbeSetting ─────────────────────────────────
            modelBuilder.Entity<ProbeSetting>(e =>
            {
                e.ToTable("WindbellProbeSettings");
                e.HasKey(p => new { p.TankNo, p.ProbeId }); // Kompozit key
                e.Property(p => p.TankNo).HasMaxLength(10);
                e.Property(p => p.ProbeId).HasMaxLength(20);
                e.Property(p => p.Version).HasMaxLength(10);
            });

            // ── SensorSetting ────────────────────────────────
            modelBuilder.Entity<SensorSetting>(e =>
            {
                e.ToTable("WindbellSensorSettings");
                e.HasKey(s => s.SensorNo);
                e.Property(s => s.SensorNo).HasMaxLength(10);
                e.Property(s => s.SensorType).HasMaxLength(10);
                e.Property(s => s.Position).HasMaxLength(10);
                e.Property(s => s.PositionNum).HasMaxLength(10);
            });

            // ── OilProductSetting ────────────────────────────
            modelBuilder.Entity<OilProductSetting>(e =>
            {
                e.ToTable("WindbellOilProductSettings");
                e.HasKey(o => o.OilCode);
                e.Property(o => o.OilCode).HasMaxLength(20);
                e.Property(o => o.OilName).HasMaxLength(50);
                e.Property(o => o.OilColor).HasMaxLength(20);
                e.Property(o => o.ExpansionRate).HasMaxLength(20);
                e.Property(o => o.Temperature).HasMaxLength(20);
                e.Property(o => o.WeightDensity).HasMaxLength(20);
            });

            // ── DensitySetting ───────────────────────────────
            modelBuilder.Entity<DensitySetting>(e =>
            {
                e.ToTable("WindbellDensitySettings");
                e.HasKey(d => d.TankNo);
                e.Property(d => d.TankNo).HasMaxLength(10);
                e.Property(d => d.Version).HasMaxLength(10);
                e.Property(d => d.HeightDiff).HasMaxLength(20);
                e.Property(d => d.FixRate).HasMaxLength(20);
                e.Property(d => d.InitDensity).HasMaxLength(20);
                e.Property(d => d.SecondDensity).HasMaxLength(20);
                e.Property(d => d.DensityFloatNo).HasMaxLength(20);
                e.Property(d => d.Remark).HasMaxLength(200);
            });

            // ── GasSensorSetting ─────────────────────────────
            modelBuilder.Entity<GasSensorSetting>(e =>
            {
                e.ToTable("WindbellGasSensorSettings");
                e.HasKey(g => g.SensorNo);
                e.Property(g => g.SensorNo).HasMaxLength(10);
                e.Property(g => g.Position).HasMaxLength(10);
                e.Property(g => g.PositionNum).HasMaxLength(10);
            });

            // ── TankTableEntry ───────────────────────────────
            modelBuilder.Entity<TankTableEntry>(e =>
            {
                e.ToTable("WindbellTankTableEntries");
                e.HasKey(t => new { t.TankNo, t.HeightMm }); // Kompozit key
                e.Property(t => t.TankNo).HasMaxLength(10);
            });

            // ── SystemVersion (tək sətir) ────────────────────
            modelBuilder.Entity<SystemVersion>(e =>
            {
                e.ToTable("WindbellSystemVersions");
                // Başlanğıc seed data — proqram ilk işə düşəndə
                e.HasData(new SystemVersion { Id = 1 });
            });
        }
    }
}
