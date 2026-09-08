using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Enums;
using OE.Shared.Repositories;

namespace OE.Shared.Test.Repositories
{
    // BetGenius auth/fixture lookups are cache-first (IMemCache) with HTTP fallback; every failure
    // path (non-2xx, exception) must leave the cache in a state that forces a fresh attempt next
    // time rather than pinning a stale/error result (except the deliberate negative-caching of
    // "fixture not found", which IS meant to stick).
    public class BetGeniusRepositoryTests
    {
        private static FakeConfiguration Config() => new(new Dictionary<string, string?>
        {
            ["Custom:BetGenius:Uri"] = "http://localhost/",
            ["Custom:BetGenius:User"] = "user",
            ["Custom:BetGenius:Password"] = "pass",
            ["Custom:BetGenius:Key"] = "key",
            ["Custom:BetGenius:Env"] = "PROD"
        });

        private static BetGeniusRepository BuildRepository(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            FakeMemCache? memCache,
            out FakeHttpMessageHandler handler)
        {
            var client = FakeHttpMessageHandler.ClientFor(responder, out handler);
            return new BetGeniusRepository(client, Config(), memCache ?? new FakeMemCache(), NullLogger<BetGeniusRepository>.Instance);
        }

        // ---- GetBetGeniusAuthorizationToken ----

        [Fact]
        public async Task GetAuthToken_CacheHit_ReturnsCachedValueWithoutCallingHttp()
        {
            var memCache = new FakeMemCache();
            var cached = new OE.Shared.Models.Responses.BaseResponseDTO<OE.Shared.Models.Provider.BetGenius.BGAuth>
            {
                StatusCode = StatusCode.Success,
                Result = new OE.Shared.Models.Provider.BetGenius.BGAuth { IdToken = "cached-token" }
            };
            memCache.AddOrUpdateCache("bg:auth", cached, 1);
            var repository = BuildRepository(_ => throw new InvalidOperationException("should not call HTTP"), memCache, out var handler);

            var result = await repository.GetBetGeniusAuthorizationToken();

            Assert.Same(cached, result);
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task GetAuthToken_CacheMiss_Success_CachesAndReturnsToken()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"IdToken":"tok","ExpiresIn":60}""")
            }, memCache, out _);

            var result = await repository.GetBetGeniusAuthorizationToken();

            Assert.Equal(StatusCode.Success, result.StatusCode);
            Assert.Equal("tok", result.Result.IdToken);
            Assert.True(memCache.Contains("bg:auth"));
        }

        [Fact]
        public async Task GetAuthToken_HttpError_ReturnsErrorAndClearsCache()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("unauthorized")
            }, memCache, out _);

            var result = await repository.GetBetGeniusAuthorizationToken();

            Assert.Equal(StatusCode.Error, result.StatusCode);
            Assert.Equal(string.Empty, result.Result.IdToken);
            Assert.False(memCache.Contains("bg:auth"));
        }

        [Fact]
        public async Task GetAuthToken_TransportException_ReturnsErrorWithMessage()
        {
            var repository = BuildRepository(_ => throw new HttpRequestException("network down"), new FakeMemCache(), out _);

            var result = await repository.GetBetGeniusAuthorizationToken();

            Assert.Equal(StatusCode.Error, result.StatusCode);
            Assert.Contains("network down", result.Message);
        }

        // ---- GetBetGeniusFixture ----

        [Fact]
        public async Task GetFixture_CacheHit_ReturnsCachedValueWithoutCallingHttp()
        {
            var memCache = new FakeMemCache();
            var cached = new OE.Shared.Models.Responses.BaseResponseDTO<OE.Shared.Models.Provider.BetGenius.BGEvent>
            {
                StatusCode = StatusCode.Success,
                Result = new OE.Shared.Models.Provider.BetGenius.BGEvent { Id = 1 }
            };
            memCache.AddOrUpdateCache("bg:fixture:F1", cached, 1);
            var repository = BuildRepository(_ => throw new InvalidOperationException("should not call HTTP"), memCache, out var handler);

            var result = await repository.GetBetGeniusFixture("tok", "F1");

            Assert.Same(cached, result);
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task GetFixture_CacheMiss_Success_SendsAuthHeadersAndCaches()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"Id":42,"Name":"Fixture 42"}""")
            }, memCache, out var handler);

            var result = await repository.GetBetGeniusFixture("tok-123", "F1");

            Assert.Equal(StatusCode.Success, result.StatusCode);
            Assert.Equal(42, result.Result.Id);
            Assert.True(memCache.Contains("bg:fixture:F1"));
            Assert.Equal("Bearer tok-123", handler.LastRequest!.Headers.GetValues("Authorization").Single());
            Assert.Equal("key", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
        }

        [Fact]
        public async Task GetFixture_NotFoundStatus_CachesNegativeResult()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found")
            }, memCache, out _);

            var result = await repository.GetBetGeniusFixture("tok", "F1");

            Assert.Equal(StatusCode.Bet_Not_Found, result.StatusCode);
            Assert.True(memCache.Contains("bg:fixture:F1")); // negative-cached on purpose
        }

        [Fact]
        public async Task GetFixture_NonNotFoundErrorMentioningFixtureNotFound_TreatedAsNotFoundAndCached()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Fixture not found for id F1")
            }, memCache, out _);

            var result = await repository.GetBetGeniusFixture("tok", "F1");

            Assert.Equal(StatusCode.Bet_Not_Found, result.StatusCode);
            Assert.True(memCache.Contains("bg:fixture:F1"));
        }

        [Fact]
        public async Task GetFixture_GenericErrorBody_ReturnsErrorAndClearsCache()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom")
            }, memCache, out _);

            var result = await repository.GetBetGeniusFixture("tok", "F1");

            Assert.Equal(StatusCode.Error, result.StatusCode);
            Assert.False(memCache.Contains("bg:fixture:F1"));
        }

        [Fact]
        public async Task GetFixture_TransportException_ReturnsErrorAndClearsCache()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => throw new HttpRequestException("dns failure"), memCache, out _);

            var result = await repository.GetBetGeniusFixture("tok", "F1");

            Assert.Equal(StatusCode.Error, result.StatusCode);
            Assert.Contains("dns failure", result.Message);
            Assert.False(memCache.Contains("bg:fixture:F1"));
        }

        // ---- GetBetGeniusFixtures ----

        [Fact]
        public async Task GetFixtures_SinglePage_ReturnsAllData()
        {
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"Id":1},{"Id":2}],"paging":{"next":null}}""")
            }, new FakeMemCache(), out _);

            var result = await repository.GetBetGeniusFixtures("tok");

            Assert.Equal(StatusCode.Success, result.StatusCode);
            Assert.Equal(new[] { 1L, 2L }, result.Result.Data.Select(e => e.Id));
        }

        [Fact]
        public async Task GetFixtures_MultiPage_FollowsPagingNextAndCombinesData()
        {
            var repository = BuildRepository(request =>
            {
                var isFirstPage = !request.RequestUri!.ToString().Contains("page2");
                return isFirstPage
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"data":[{"Id":1}],"paging":{"next":"Video-v3/PROD/fixtures?page2"}}""")
                    }
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"data":[{"Id":2}],"paging":{"next":null}}""")
                    };
            }, new FakeMemCache(), out var handler);

            var result = await repository.GetBetGeniusFixtures("tok");

            Assert.Equal(StatusCode.Success, result.StatusCode);
            Assert.Equal(new[] { 1L, 2L }, result.Result.Data.Select(e => e.Id));
            Assert.Equal(2, handler.CallCount);
        }

        [Fact]
        public async Task GetFixtures_NotFound_ReturnsBetNotFound()
        {
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.NotFound), new FakeMemCache(), out _);

            var result = await repository.GetBetGeniusFixtures("tok");

            Assert.Equal(StatusCode.Bet_Not_Found, result.StatusCode);
        }

        [Fact]
        public async Task GetFixtures_GenericError_ReturnsErrorWithResponseBodyAsMessage()
        {
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream exploded")
            }, new FakeMemCache(), out _);

            var result = await repository.GetBetGeniusFixtures("tok");

            Assert.Equal(StatusCode.Error, result.StatusCode);
            Assert.Equal("upstream exploded", result.Message);
        }

        [Fact]
        public async Task GetFixtures_TransportException_ReturnsErrorWithMessage()
        {
            var repository = BuildRepository(_ => throw new HttpRequestException("connection reset"), new FakeMemCache(), out _);

            var result = await repository.GetBetGeniusFixtures("tok");

            Assert.Equal(StatusCode.Error, result.StatusCode);
            Assert.Contains("connection reset", result.Message);
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        // ---- GetBetGeniusAuthorizationToken ----

        [Fact]
        public async Task GetAuthToken_CacheHit_ReturnsCachedValueWithoutCallingHttp_MutationCheck()
        {
            var memCache = new FakeMemCache();
            var cached = new OE.Shared.Models.Responses.BaseResponseDTO<OE.Shared.Models.Provider.BetGenius.BGAuth>
            {
                StatusCode = StatusCode.Success,
                Result = new OE.Shared.Models.Provider.BetGenius.BGAuth { IdToken = "cached-token" }
            };
            memCache.AddOrUpdateCache("bg:auth", cached, 1);
            var repository = BuildRepository(_ => throw new InvalidOperationException("should not call HTTP"), memCache, out var handler);

            var result = await repository.GetBetGeniusAuthorizationToken();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotSame(cached, result));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, handler.CallCount));
        }

        [Fact]
        public async Task GetAuthToken_CacheMiss_Success_CachesAndReturnsToken_MutationCheck()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"IdToken":"tok","ExpiresIn":60}""")
            }, memCache, out _);

            var result = await repository.GetBetGeniusAuthorizationToken();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Error, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong-token", result.Result.IdToken));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(memCache.Contains("bg:auth")));
        }

        [Fact]
        public async Task GetAuthToken_HttpError_ReturnsErrorAndClearsCache_MutationCheck()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("unauthorized")
            }, memCache, out _);

            var result = await repository.GetBetGeniusAuthorizationToken();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("something", result.Result.IdToken));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(memCache.Contains("bg:auth")));
        }

        [Fact]
        public async Task GetAuthToken_TransportException_ReturnsErrorWithMessage_MutationCheck()
        {
            var repository = BuildRepository(_ => throw new HttpRequestException("network down"), new FakeMemCache(), out _);

            var result = await repository.GetBetGeniusAuthorizationToken();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain("network down", result.Message));
        }

        // ---- GetBetGeniusFixture ----

        [Fact]
        public async Task GetFixture_CacheHit_ReturnsCachedValueWithoutCallingHttp_MutationCheck()
        {
            var memCache = new FakeMemCache();
            var cached = new OE.Shared.Models.Responses.BaseResponseDTO<OE.Shared.Models.Provider.BetGenius.BGEvent>
            {
                StatusCode = StatusCode.Success,
                Result = new OE.Shared.Models.Provider.BetGenius.BGEvent { Id = 1 }
            };
            memCache.AddOrUpdateCache("bg:fixture:F1", cached, 1);
            var repository = BuildRepository(_ => throw new InvalidOperationException("should not call HTTP"), memCache, out var handler);

            var result = await repository.GetBetGeniusFixture("tok", "F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotSame(cached, result));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, handler.CallCount));
        }

        [Fact]
        public async Task GetFixture_CacheMiss_Success_SendsAuthHeadersAndCaches_MutationCheck()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"Id":42,"Name":"Fixture 42"}""")
            }, memCache, out var handler);

            var result = await repository.GetBetGeniusFixture("tok-123", "F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Error, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(43, result.Result.Id));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(memCache.Contains("bg:fixture:F1")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("Bearer wrong-token", handler.LastRequest!.Headers.GetValues("Authorization").Single()));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong-key", handler.LastRequest!.Headers.GetValues("x-api-key").Single()));
        }

        [Fact]
        public async Task GetFixture_NotFoundStatus_CachesNegativeResult_MutationCheck()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found")
            }, memCache, out _);

            var result = await repository.GetBetGeniusFixture("tok", "F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(memCache.Contains("bg:fixture:F1"))); // negative-cached on purpose
        }

        [Fact]
        public async Task GetFixture_NonNotFoundErrorMentioningFixtureNotFound_TreatedAsNotFoundAndCached_MutationCheck()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Fixture not found for id F1")
            }, memCache, out _);

            var result = await repository.GetBetGeniusFixture("tok", "F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Error, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(memCache.Contains("bg:fixture:F1")));
        }

        [Fact]
        public async Task GetFixture_GenericErrorBody_ReturnsErrorAndClearsCache_MutationCheck()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom")
            }, memCache, out _);

            var result = await repository.GetBetGeniusFixture("tok", "F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(memCache.Contains("bg:fixture:F1")));
        }

        [Fact]
        public async Task GetFixture_TransportException_ReturnsErrorAndClearsCache_MutationCheck()
        {
            var memCache = new FakeMemCache();
            var repository = BuildRepository(_ => throw new HttpRequestException("dns failure"), memCache, out _);

            var result = await repository.GetBetGeniusFixture("tok", "F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain("dns failure", result.Message));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(memCache.Contains("bg:fixture:F1")));
        }

        // ---- GetBetGeniusFixtures ----

        [Fact]
        public async Task GetFixtures_SinglePage_ReturnsAllData_MutationCheck()
        {
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"Id":1},{"Id":2}],"paging":{"next":null}}""")
            }, new FakeMemCache(), out _);

            var result = await repository.GetBetGeniusFixtures("tok");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Error, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(new[] { 1L, 3L }, result.Result.Data.Select(e => e.Id)));
        }

        [Fact]
        public async Task GetFixtures_MultiPage_FollowsPagingNextAndCombinesData_MutationCheck()
        {
            var repository = BuildRepository(request =>
            {
                var isFirstPage = !request.RequestUri!.ToString().Contains("page2");
                return isFirstPage
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"data":[{"Id":1}],"paging":{"next":"Video-v3/PROD/fixtures?page2"}}""")
                    }
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"data":[{"Id":2}],"paging":{"next":null}}""")
                    };
            }, new FakeMemCache(), out var handler);

            var result = await repository.GetBetGeniusFixtures("tok");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Error, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(new[] { 1L }, result.Result.Data.Select(e => e.Id)));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, handler.CallCount));
        }

        [Fact]
        public async Task GetFixtures_NotFound_ReturnsBetNotFound_MutationCheck()
        {
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.NotFound), new FakeMemCache(), out _);

            var result = await repository.GetBetGeniusFixtures("tok");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
        }

        [Fact]
        public async Task GetFixtures_GenericError_ReturnsErrorWithResponseBodyAsMessage_MutationCheck()
        {
            var repository = BuildRepository(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream exploded")
            }, new FakeMemCache(), out _);

            var result = await repository.GetBetGeniusFixtures("tok");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("something else", result.Message));
        }

        [Fact]
        public async Task GetFixtures_TransportException_ReturnsErrorWithMessage_MutationCheck()
        {
            var repository = BuildRepository(_ => throw new HttpRequestException("connection reset"), new FakeMemCache(), out _);

            var result = await repository.GetBetGeniusFixtures("tok");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain("connection reset", result.Message));
        }

        #endregion
    }
}
