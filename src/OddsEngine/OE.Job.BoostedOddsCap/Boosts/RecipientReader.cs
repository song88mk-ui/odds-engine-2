using Microsoft.EntityFrameworkCore;
using OE.Job.BoostedOddsCap.Data;

namespace OE.Job.BoostedOddsCap.Boosts
{
    public interface IRecipientReader
    {
        Task<IReadOnlyList<ClientRecipientRow>> ReadAsync(CancellationToken cancellationToken);
    }

    // Reads the clients that have the Notify email switch on from the Backoffice database. The connection string is
    // only touched on the first read.
    public class SqlRecipientReader : IRecipientReader
    {
        private readonly string _connectionString;
        private readonly int _commandTimeoutSeconds;
        private DbContextOptions<BackofficeContext> _options;

        public SqlRecipientReader(string connectionString, int commandTimeoutSeconds)
        {
            _connectionString = connectionString;
            _commandTimeoutSeconds = commandTimeoutSeconds;
        }

        public async Task<IReadOnlyList<ClientRecipientRow>> ReadAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                throw new InvalidOperationException("ConnectionStrings:Backoffice is not set, so the clients to email cannot be read");

            _options ??= new DbContextOptionsBuilder<BackofficeContext>()
                    .UseSqlServer(_connectionString, sql => sql.CommandTimeout(_commandTimeoutSeconds))
                    .Options;

            using var context = new BackofficeContext(_options);

            // RecipientRules checks the same two columns again, this only keeps the rows that cannot qualify off the wire
            return await context.ClientInformation.AsNoTracking()
                    .Where(c => !c.IsDeleted && c.IsEmailNotification)
                    .ToListAsync(cancellationToken);
        }
    }

    // Used when email is off: nobody is emailed, so the Backoffice database is never opened and the job needs no
    // connection string for it.
    public class NoRecipientReader : IRecipientReader
    {
        public Task<IReadOnlyList<ClientRecipientRow>> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ClientRecipientRow>>(new ClientRecipientRow[0]);
        }
    }
}
