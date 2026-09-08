using OE.Shared.Utilities;

namespace OE.Shared.Test.Utilities
{
    // MemCache is a thin wrapper over the real, process-wide MemoryCache.Default — there's no seam
    // to fake it out, so every test uses a fresh Guid-based key to avoid colliding with any other
    // test (this class or another) touching the same static cache.
    public class MemCacheTests
    {
        private readonly MemCache _cache = new();

        private static string NewKey() => $"memcache-test:{Guid.NewGuid()}";

        [Fact]
        public void AddOrUpdateCache_WithHoursTimeout_RoundTripsThroughGetCache()
        {
            var key = NewKey();

            _cache.AddOrUpdateCache(key, "hello", 1);

            Assert.Equal("hello", _cache.GetCache<string>(key));
        }

        [Fact]
        public void AddOrUpdateCache_WithAbsoluteDate_RoundTripsThroughGetCache()
        {
            var key = NewKey();

            _cache.AddOrUpdateCache(key, "hello", DateTimeOffset.UtcNow.AddHours(1));

            Assert.Equal("hello", _cache.GetCache<string>(key));
        }

        [Fact]
        public void GetCache_MissingKey_ReturnsNull()
        {
            Assert.Null(_cache.GetCache<string>(NewKey()));
        }

        [Fact]
        public void GetCache_WithRepositoryCall_OnMiss_InvokesCallAndPopulatesCache()
        {
            var key = NewKey();
            var calls = 0;

            var first = _cache.GetCache(key, () => { calls++; return "computed"; }, 1);
            var second = _cache.GetCache(key, () => { calls++; return "computed"; }, 1);

            Assert.Equal("computed", first);
            Assert.Equal("computed", second);
            Assert.Equal(1, calls); // second call is a cache hit, repositoryCall not invoked again
        }

        [Fact]
        public void GetCacheData_WorksForValueTypesWithNoClassConstraint()
        {
            var key = NewKey();

            _cache.AddOrUpdateCache(key, 42, 1);

            Assert.Equal(42, _cache.GetCacheData<int>(key));
        }

        // GetCacheData<T> has no `where T : class` constraint, but the cast `(T)result` still runs
        // against a possibly-null object — for a value type T on a miss, that's a null-to-value-type
        // cast, which throws rather than returning default(T). Documents the existing quirk rather
        // than an idealized behavior.
        [Fact]
        public void GetCacheData_MissingKey_ValueType_Throws()
        {
            Assert.Throws<NullReferenceException>(() => _cache.GetCacheData<int>(NewKey()));
        }

        [Fact]
        public void GetCache_KeyValuePairOverload_MissingKey_ReturnsFalseZero()
        {
            var result = _cache.GetCache(NewKey());

            Assert.Equal(new KeyValuePair<bool, long>(false, 0), result);
        }

        [Fact]
        public void GetCache_KeyValuePairOverload_HitReturnsStoredPair()
        {
            var key = NewKey();
            var stored = new KeyValuePair<bool, long>(true, 99);

            _cache.AddOrUpdateCache(key, stored, 1);

            Assert.Equal(stored, _cache.GetCache(key));
        }

        [Fact]
        public void DeleteCache_RemovesEntry()
        {
            var key = NewKey();
            _cache.AddOrUpdateCache(key, "hello", 1);

            _cache.DeleteCache(key);

            Assert.Null(_cache.GetCache<string>(key));
            Assert.False(_cache.Contains(key));
        }

        [Fact]
        public void Contains_ReflectsPresenceOfKey()
        {
            var key = NewKey();

            Assert.False(_cache.Contains(key));

            _cache.AddOrUpdateCache(key, "hello", 1);

            Assert.True(_cache.Contains(key));
        }

        [Fact]
        public void GetExistingKeys_ReturnsKeysContainingSubstring()
        {
            var marker = $"marker:{Guid.NewGuid()}";
            var matchingKey = $"prefix:{marker}:1";
            var otherKey = NewKey();

            _cache.AddOrUpdateCache(matchingKey, "a", 1);
            _cache.AddOrUpdateCache(otherKey, "b", 1);

            var keys = _cache.GetExistingKeys(marker);

            Assert.Contains(matchingKey, keys);
            Assert.DoesNotContain(otherKey, keys);
        }

        [Fact]
        public void DeleteCacheByPrefix_OnlyRemovesMatchingKeys()
        {
            var prefix = $"prefix:{Guid.NewGuid()}:";
            var matching1 = $"{prefix}a";
            var matching2 = $"{prefix}b";
            var unrelated = NewKey();

            _cache.AddOrUpdateCache(matching1, "a", 1);
            _cache.AddOrUpdateCache(matching2, "b", 1);
            _cache.AddOrUpdateCache(unrelated, "c", 1);

            _cache.DeleteCacheByPrefix(prefix);

            Assert.False(_cache.Contains(matching1));
            Assert.False(_cache.Contains(matching2));
            Assert.True(_cache.Contains(unrelated));
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void AddOrUpdateCache_WithHoursTimeout_RoundTripsThroughGetCache_MutationCheck()
        {
            var key = NewKey();

            _cache.AddOrUpdateCache(key, "hello", 1);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("goodbye", _cache.GetCache<string>(key)));
        }

        [Fact]
        public void AddOrUpdateCache_WithAbsoluteDate_RoundTripsThroughGetCache_MutationCheck()
        {
            var key = NewKey();

            _cache.AddOrUpdateCache(key, "hello", DateTimeOffset.UtcNow.AddHours(1));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("goodbye", _cache.GetCache<string>(key)));
        }

        [Fact]
        public void GetCache_MissingKey_ReturnsNull_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(_cache.GetCache<string>(NewKey())));
        }

        [Fact]
        public void GetCache_WithRepositoryCall_OnMiss_InvokesCallAndPopulatesCache_MutationCheck()
        {
            var key = NewKey();
            var calls = 0;

            var first = _cache.GetCache(key, () => { calls++; return "computed"; }, 1);
            var second = _cache.GetCache(key, () => { calls++; return "computed"; }, 1);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong", first));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong", second));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(2, calls)); // second call is a cache hit, repositoryCall not invoked again
        }

        [Fact]
        public void GetCacheData_WorksForValueTypesWithNoClassConstraint_MutationCheck()
        {
            var key = NewKey();

            _cache.AddOrUpdateCache(key, 42, 1);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(43, _cache.GetCacheData<int>(key)));
        }

        [Fact]
        public void GetCacheData_MissingKey_ValueType_Throws_MutationCheck()
        {
            // The real test proves GetCacheData<int> throws NullReferenceException on a miss. This
            // proves the proof itself discriminates: asserting that NO exception was thrown must
            // fail here, since one really is.
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            {
                var exception = Record.Exception(() => _cache.GetCacheData<int>(NewKey()));
                Assert.Null(exception);
            });
        }

        [Fact]
        public void GetCache_KeyValuePairOverload_MissingKey_ReturnsFalseZero_MutationCheck()
        {
            var result = _cache.GetCache(NewKey());

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(new KeyValuePair<bool, long>(true, 1), result));
        }

        [Fact]
        public void GetCache_KeyValuePairOverload_HitReturnsStoredPair_MutationCheck()
        {
            var key = NewKey();
            var stored = new KeyValuePair<bool, long>(true, 99);

            _cache.AddOrUpdateCache(key, stored, 1);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(new KeyValuePair<bool, long>(false, 0), _cache.GetCache(key)));
        }

        [Fact]
        public void DeleteCache_RemovesEntry_MutationCheck()
        {
            var key = NewKey();
            _cache.AddOrUpdateCache(key, "hello", 1);

            _cache.DeleteCache(key);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(_cache.GetCache<string>(key)));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(_cache.Contains(key)));
        }

        [Fact]
        public void Contains_ReflectsPresenceOfKey_MutationCheck()
        {
            var key = NewKey();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(_cache.Contains(key)));

            _cache.AddOrUpdateCache(key, "hello", 1);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(_cache.Contains(key)));
        }

        [Fact]
        public void GetExistingKeys_ReturnsKeysContainingSubstring_MutationCheck()
        {
            var marker = $"marker:{Guid.NewGuid()}";
            var matchingKey = $"prefix:{marker}:1";
            var otherKey = NewKey();

            _cache.AddOrUpdateCache(matchingKey, "a", 1);
            _cache.AddOrUpdateCache(otherKey, "b", 1);

            var keys = _cache.GetExistingKeys(marker);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain(matchingKey, keys));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Contains(otherKey, keys));
        }

        [Fact]
        public void DeleteCacheByPrefix_OnlyRemovesMatchingKeys_MutationCheck()
        {
            var prefix = $"prefix:{Guid.NewGuid()}:";
            var matching1 = $"{prefix}a";
            var matching2 = $"{prefix}b";
            var unrelated = NewKey();

            _cache.AddOrUpdateCache(matching1, "a", 1);
            _cache.AddOrUpdateCache(matching2, "b", 1);
            _cache.AddOrUpdateCache(unrelated, "c", 1);

            _cache.DeleteCacheByPrefix(prefix);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(_cache.Contains(matching1)));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(_cache.Contains(matching2)));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(_cache.Contains(unrelated)));
        }

        #endregion
    }
}
