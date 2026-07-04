using OE.Shared.Utilities;

namespace OE.Translation.Service.Tests
{
    // No-op cache: GetCache always misses so the repository's per-key throttles never skip a write,
    // making every test deterministic regardless of ordering.
    public class FakeMemCache : IMemCache
    {
        public void AddOrUpdateCache<T>(string key, T cacheObject, int cacheTimeout) { }

        public void AddOrUpdateCache<T>(string key, T cacheObject, DateTimeOffset date) { }

        public T GetCache<T>(string key, Func<T> repositoryCall, int expiration) where T : class => repositoryCall();

        public T? GetCache<T>(string key) where T : class => null;

        public T? GetCacheData<T>(string key) => default;

        public KeyValuePair<bool, long> GetCache(string key) => new(false, 0);

        public void DeleteCache(string key) { }

        public bool Contains(string key) => false;

        public List<string> GetExistingKeys(string name) => new();

        public void DeleteCacheByPrefix(string prefix) { }
    }
}
