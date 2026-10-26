using Microsoft.EntityFrameworkCore;
using OE.Job.BoostedOddsCap.Data;

namespace OE.Job.BoostedOddsCap.Boosts
{
    public sealed class TradingBoostData
    {
        public IReadOnlyList<PriceBoostRow> Boosts { get; init; }
        public IReadOnlyList<BoostedSettingsRow> Settings { get; init; }
    }

    public interface ITradingBoostReader
    {
        Task<TradingBoostData> ReadAsync(CancellationToken cancellationToken);
    }

    // Reads the two small Trading tables the cap check cannot do without, with no tracking. The connection string is
    // only touched on the first read, so a disabled job starts without one.
    public class SqlTradingBoostReader : ITradingBoostReader
    {
        private readonly string _connectionString;
        private readonly int _commandTimeoutSeconds;
        private DbContextOptions<BoostContext> _options;

        public SqlTradingBoostReader(string connectionString, int commandTimeoutSeconds)
        {
            _connectionString = connectionString;
            _commandTimeoutSeconds = commandTimeoutSeconds;
        }

        public async Task<TradingBoostData> ReadAsync(CancellationToken cancellationToken)
        {
            _options ??= new DbContextOptionsBuilder<BoostContext>()
                    .UseSqlServer(_connectionString, sql => sql.CommandTimeout(_commandTimeoutSeconds))
                    .Options;

            using var context = new BoostContext(_options);

            return new TradingBoostData
            {
                Boosts = await context.PriceBoosts.AsNoTracking().ToListAsync(cancellationToken),
                Settings = await context.BoostedSettings.AsNoTracking().ToListAsync(cancellationToken)
            };
        }
    }
}
