using OE.Shared.Utilities;

namespace OE.Shared.Test
{
    // Stateful, in-process stand-in for IMemCache (a Dictionary, not the real static
    // MemoryCache.Default) so repository tests can exercise both the cache-hit and cache-miss
    // branches deterministically. Timeouts/expiry are ignored — nothing here ever expires.
    public class FakeMemCache : IMemCache
    {
        private readonly Dictionary<string, object> _store = new();

        public void AddOrUpdateCache<T>(string key, T cacheObject, int cacheTimeout) => _store[key] = cacheObject!;

        public void AddOrUpdateCache<T>(string key, T cacheObject, DateTimeOffset date) => _store[key] = cacheObject!;

        public T GetCache<T>(string key, Func<T> repositoryCall, int expiration) where T : class
        {
            if (_store.TryGetValue(key, out var cached))
                return (T)cached;

            var result = repositoryCall();
            if (result != null)
                AddOrUpdateCache(key, result, expiration);

            return result;
        }

        public T? GetCache<T>(string key) where T : class =>
            _store.TryGetValue(key, out var cached) ? (T)cached : null;

        public T? GetCacheData<T>(string key) =>
            _store.TryGetValue(key, out var cached) ? (T)cached : default;

        public KeyValuePair<bool, long> GetCache(string key) =>
            _store.TryGetValue(key, out var cached) ? (KeyValuePair<bool, long>)cached : new KeyValuePair<bool, long>(false, 0);

        public void DeleteCache(string key) => _store.Remove(key);

        public bool Contains(string key) => _store.ContainsKey(key);

        public List<string> GetExistingKeys(string name) => _store.Keys.Where(k => k.Contains(name)).ToList();

        public void DeleteCacheByPrefix(string prefix)
        {
            foreach (var key in _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                _store.Remove(key);
        }
    }
}
