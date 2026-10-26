using Microsoft.EntityFrameworkCore;

namespace OE.Job.BoostedOddsCap.Data
{
    // Read-only EF context over the Trading SQL database. backoffice-final owns and writes the two
    // tables (Backoffice.Shared/Data/Trading/SQLEntities); only the columns the cap check reads are
    // mapped, and every entity is keyless because the job never saves or tracks anything.
    public class BoostContext : DbContext
    {
        public BoostContext(DbContextOptions<BoostContext> options) : base(options)
        {
        }

        public DbSet<PriceBoostRow> PriceBoosts { get; set; }
        public DbSet<BoostedSettingsRow> BoostedSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PriceBoostRow>().HasNoKey().ToTable("PriceBoostConfig");
            modelBuilder.Entity<BoostedSettingsRow>().HasNoKey().ToTable("BoostedSettings");
        }
    }

    // Minimal projection of Backoffice.Shared PriceBoostConfig: one boosted selection of one client.
    public class PriceBoostRow
    {
        public string ClientCode { get; set; }
        public string ProviderFixtureId { get; set; }
        public string MarketTemplateId { get; set; }
        public string MarketSpecifier { get; set; }
        public string SelectionTemplateId { get; set; }
        public string SelectionSpecifier { get; set; }

        // Every column of these tables is NULL-able in SQL (Flyway V1__Baseline). Backoffice never writes a null,
        // but a decimal here would make EF throw on the first one it met and fail the whole refresh, so the
        // snapshot builder skips such a row instead.
        public decimal? BoostedOdds { get; set; }
    }

    // Minimal projection of Backoffice.Shared BoostedSettings. Cap is a fraction (0.05 is 5%), the
    // Boosted Settings page shows it multiplied by 100.
    public class BoostedSettingsRow
    {
        public string ClientCode { get; set; }
        public decimal? Cap { get; set; }
    }
}
