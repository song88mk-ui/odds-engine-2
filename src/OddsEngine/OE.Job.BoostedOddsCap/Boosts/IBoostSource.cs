using Microsoft.EntityFrameworkCore;
using OE.Job.BoostedOddsCap.Data;

namespace OE.Job.BoostedOddsCap.Boosts
{
    public interface IBoostSource
    {
        Task<BoostSnapshot> LoadAsync(CancellationToken cancellationToken);
    }

    // Reads the three small Trading tables with no tracking and builds a fresh snapshot. The connection
    // string is only touched on the first load, so a disabled job starts without one.
    public class SqlBoostSource : IBoostSource
    {
        private readonly string _connectionString;
        private readonly int _commandTimeoutSeconds;
        private DbContextOptions<BoostContext> _options;

        public SqlBoostSource(string connectionString, int commandTimeoutSeconds)
        {
            _connectionString = connectionString;
            _commandTimeoutSeconds = commandTimeoutSeconds;
        }

        public async Task<BoostSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            _options ??= new DbContextOptionsBuilder<BoostContext>()
                    .UseSqlServer(_connectionString, sql => sql.CommandTimeout(_commandTimeoutSeconds))
                    .Options;

            using var context = new BoostContext(_options);

            var boosts = await context.PriceBoosts.AsNoTracking().ToListAsync(cancellationToken);
            var settings = await context.BoostedSettings.AsNoTracking().ToListAsync(cancellationToken);
            var clients = await context.Clients.AsNoTracking().ToListAsync(cancellationToken);

            return BoostSnapshot.Build(boosts, settings, clients, DateTime.UtcNow);
        }
    }
}
