using Microsoft.Extensions.Logging;

namespace OE.Job.BoostedOddsCap.Boosts
{
    // Builds the snapshot from the two databases it needs. The boosts and caps are what the job exists for, so a
    // failure to read them fails the load and the previous snapshot stays in use. The recipients only matter for the
    // optional email, so a failure to read them must not take the toast down with it: the previous recipients are
    // kept and the failure is logged, and the next load tries again.
    public class BoostSnapshotLoader : IBoostSource
    {
        private readonly ITradingBoostReader _tradingReader;
        private readonly IRecipientReader _recipientReader;
        private readonly ILogger<BoostSnapshotLoader> _logger;

        // read and written by the one refresher thread only
        private IReadOnlyDictionary<string, string[]> _lastRecipients = new Dictionary<string, string[]>();

        public BoostSnapshotLoader(ITradingBoostReader tradingReader, IRecipientReader recipientReader,
            ILogger<BoostSnapshotLoader> logger)
        {
            _tradingReader = tradingReader;
            _recipientReader = recipientReader;
            _logger = logger;
        }

        public async Task<BoostSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            var trading = await _tradingReader.ReadAsync(cancellationToken);
            var recipients = await ReadRecipientsAsync(cancellationToken);

            return BoostSnapshot.Build(trading.Boosts, trading.Settings, recipients, DateTime.UtcNow);
        }

        private async Task<IReadOnlyDictionary<string, string[]>> ReadRecipientsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var rows = await _recipientReader.ReadAsync(cancellationToken);
                _lastRecipients = RecipientRules.Build(rows);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reading the clients to email failed, keeping the {RecipientCount} clients of the previous load",
                        _lastRecipients.Count);
            }

            return _lastRecipients;
        }
    }
}
