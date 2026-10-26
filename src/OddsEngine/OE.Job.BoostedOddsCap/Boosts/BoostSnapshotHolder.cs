namespace OE.Job.BoostedOddsCap.Boosts
{
    // The one reference the consumer thread reads and the refresher replaces. A volatile reference to an
    // immutable snapshot is all the synchronisation the hot path needs.
    public class BoostSnapshotHolder
    {
        private readonly TaskCompletionSource<bool> _firstLoad =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private volatile BoostSnapshot _current = BoostSnapshot.Empty;

        public BoostSnapshot Current => _current;

        // Completes after the first successful load: until then the consumer must not start, or the odds it
        // would judge against an empty index could never alert.
        public Task FirstLoad => _firstLoad.Task;

        public void Set(BoostSnapshot snapshot)
        {
            _current = snapshot;
            _firstLoad.TrySetResult(true);
        }
    }
}
