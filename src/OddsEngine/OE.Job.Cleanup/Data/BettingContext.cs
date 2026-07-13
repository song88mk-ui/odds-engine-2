using Microsoft.EntityFrameworkCore;
using System;

namespace OE.Job.Cleanup.Data
{
    // Read-only EF context over the platform Betting SQL database (owned by platform-final's
    // Betting.API). Only the two columns needed to preload fixtures-with-bets are mapped.
    public class BettingContext : DbContext
    {
        public BettingContext(DbContextOptions<BettingContext> options) : base(options)
        {
        }

        public DbSet<BetSelection> BetSelection { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BetSelection>().HasKey(k => k.BetSelectionId);
        }
    }

    // Minimal projection of Platform.Shared.Data.Betting.Entities.BetSelection.
    public class BetSelection
    {
        public Guid BetSelectionId { get; set; }
        public long FixtureId { get; set; }
    }
}
