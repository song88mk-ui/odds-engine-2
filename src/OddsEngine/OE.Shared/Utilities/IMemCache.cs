using System.Collections.Generic;
using System;

namespace OE.Shared.Utilities
{
    public interface IMemCache
    {
        void AddOrUpdateCache<T>(string key, T cacheObject, int cacheTimeout);

        void AddOrUpdateCache<T>(string key, T cacheObject, DateTimeOffset date);

        T GetCache<T>(string key, Func<T> repositoryCall, int expiration) where T : class;

        T GetCache<T>(string key) where T : class;

        T GetCacheData<T>(string key);

        KeyValuePair<bool, long> GetCache(string key);

        void DeleteCache(string key);

        bool Contains(string key);

        List<string> GetExistingKeys(string name);

        void DeleteCacheByPrefix(string prefix);
    }
}
