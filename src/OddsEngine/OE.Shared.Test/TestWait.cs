namespace OE.Shared.Test
{
    // Polls a condition instead of sleeping a fixed duration, so a slow CI box makes the wait
    // longer instead of making the assertion flaky.
    public static class TestWait
    {
        public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, TimeSpan? pollInterval = null)
        {
            var interval = pollInterval ?? TimeSpan.FromMilliseconds(20);
            var deadline = DateTime.UtcNow + timeout;
            while (!condition() && DateTime.UtcNow < deadline)
                await Task.Delay(interval);
        }
    }
}
