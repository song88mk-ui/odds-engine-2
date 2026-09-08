using Microsoft.EntityFrameworkCore;
using OE.Shared.Data.Trading.Entities;

namespace OE.Shared.Data.Trading
{
    public class TradingContext : DbContext
    {
        public TradingContext(DbContextOptions<TradingContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ClientOddsFeedConfiguration>().HasKey(k => new { k.ClientId, k.SportId });
            modelBuilder.Entity<ClientOddsFeedLiveFixture>().HasKey(k => new { k.ClientId, k.SportId, k.FixtureSpecifier });

        }

        public DbSet<ClientOddsFeedConfiguration> ClientOddsFeedConfiguration { get; set; }
        public DbSet<ClientOddsFeedLiveFixture> ClientOddsFeedLiveFixture { get; set; }

    }
}
