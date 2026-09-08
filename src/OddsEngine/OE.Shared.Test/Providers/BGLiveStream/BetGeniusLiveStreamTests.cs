using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Enums;
using OE.Shared.Models.Provider.BetGenius;
using OE.Shared.Models.Responses;
using OE.Shared.Providers.BGLiveStream;

namespace OE.Shared.Test.Providers.BGLiveStream
{
    // Every failure mode here (auth failure, null/error fixture results, no streams available,
    // and any thrown exception) degrades to an empty/false result rather than propagating — this
    // provider is polled by the LiveStream job, so a bad fixture must never take the whole poll
    // loop down.
    public class BetGeniusLiveStreamTests
    {
        private static OE.Shared.Providers.BGLiveStream.BetGeniusLiveStream BuildProvider(FakeBetGeniusRepository repo) =>
            new(new FakeConfiguration(new Dictionary<string, string?>()), NullLogger<OE.Shared.Providers.BGLiveStream.BetGeniusLiveStream>.Instance, repo);

        private static BaseResponseDTO<BGAuth> SuccessAuth() => new()
        {
            StatusCode = StatusCode.Success,
            Result = new BGAuth { IdToken = "tok" }
        };

        // ---- GetLiveStreams ----

        // Each case differs only in how the fake repository is configured; the real Theory and
        // its MutationCheck companion below both drive off this same data.
        public static IEnumerable<object[]> LiveStreamsEmptyResultCases()
        {
            yield return new object[]
            {
                "AuthFailure",
                new FakeBetGeniusRepository
                {
                    AuthResponder = () => Task.FromResult(new BaseResponseDTO<BGAuth> { StatusCode = StatusCode.Error })
                }
            };
            yield return new object[]
            {
                "FixturesResultIsNull",
                new FakeBetGeniusRepository
                {
                    AuthResponder = () => Task.FromResult(SuccessAuth()),
                    FixturesResponder = _ => Task.FromResult<BaseResponseDTO<BGEvents>>(null!)
                }
            };
            yield return new object[]
            {
                "FixturesError",
                new FakeBetGeniusRepository
                {
                    AuthResponder = () => Task.FromResult(SuccessAuth()),
                    FixturesResponder = _ => Task.FromResult(new BaseResponseDTO<BGEvents> { StatusCode = StatusCode.Error })
                }
            };
            yield return new object[]
            {
                "NoFixtureData",
                new FakeBetGeniusRepository
                {
                    AuthResponder = () => Task.FromResult(SuccessAuth()),
                    FixturesResponder = _ => Task.FromResult(new BaseResponseDTO<BGEvents>
                    {
                        StatusCode = StatusCode.Success,
                        Result = new BGEvents { Data = new List<BGEvent>() }
                    })
                }
            };
        }

        [Theory]
        [MemberData(nameof(LiveStreamsEmptyResultCases))]
        public async Task GetLiveStreams_ReturnsEmptyList(string _, FakeBetGeniusRepository repo)
        {
            var result = await BuildProvider(repo).GetLiveStreams();

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetLiveStreams_Success_ReturnsFixtureIdsAsStrings()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(SuccessAuth()),
                FixturesResponder = _ => Task.FromResult(new BaseResponseDTO<BGEvents>
                {
                    StatusCode = StatusCode.Success,
                    Result = new BGEvents { Data = new List<BGEvent> { new() { Id = 1 }, new() { Id = 2 } } }
                })
            };

            var result = await BuildProvider(repo).GetLiveStreams();

            Assert.Equal(new[] { "1", "2" }, result);
        }

        [Fact]
        public async Task GetLiveStreams_RepositoryThrows_ReturnsEmptyList()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => throw new InvalidOperationException("boom")
            };

            var result = await BuildProvider(repo).GetLiveStreams();

            Assert.Empty(result);
        }

        // ---- GetLiveStream ----

        [Fact]
        public async Task GetLiveStream_AuthFailure_ReturnsFalseAndEmptyString()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(new BaseResponseDTO<BGAuth> { StatusCode = StatusCode.Error })
            };

            var (success, payload) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.False(success);
            Assert.Equal(string.Empty, payload);
        }

        [Fact]
        public async Task GetLiveStream_FixtureResultNull_ReturnsFalse()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(SuccessAuth()),
                FixtureResponder = (_, _) => Task.FromResult<BaseResponseDTO<BGEvent>>(null!)
            };

            var (success, _) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.False(success);
        }

        [Fact]
        public async Task GetLiveStream_FixtureNotFound_ReturnsFalse()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(SuccessAuth()),
                FixtureResponder = (_, _) => Task.FromResult(new BaseResponseDTO<BGEvent> { StatusCode = StatusCode.Bet_Not_Found })
            };

            var (success, _) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.False(success);
        }

        [Fact]
        public async Task GetLiveStream_NoLiveStreamsOnFixture_ReturnsFalse()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(SuccessAuth()),
                FixtureResponder = (_, _) => Task.FromResult(new BaseResponseDTO<BGEvent>
                {
                    StatusCode = StatusCode.Success,
                    Result = new BGEvent { Id = 1, LiveStreams = new List<LiveStreamDto>() }
                })
            };

            var (success, payload) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.False(success);
            Assert.Equal(string.Empty, payload);
        }

        [Fact]
        public async Task GetLiveStream_Success_ReturnsTrueWithSerializedStreams()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(SuccessAuth()),
                FixtureResponder = (_, _) => Task.FromResult(new BaseResponseDTO<BGEvent>
                {
                    StatusCode = StatusCode.Success,
                    Result = new BGEvent
                    {
                        Id = 1,
                        LiveStreams = new List<LiveStreamDto>
                        {
                            new()
                            {
                                Id = "stream-1",
                                Permissions = new List<PermissionDto> { new() { Device = "web", Region = "US" } },
                                Deliveries = new DeliveriesDto()
                            }
                        }
                    }
                })
            };

            var (success, payload) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.True(success);
            Assert.Contains("stream-1", payload);
            Assert.Contains("web:US", payload);
        }

        [Fact]
        public async Task GetLiveStream_RepositoryThrows_ReturnsFalse()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => throw new InvalidOperationException("boom")
            };

            var (success, payload) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.False(success);
            Assert.Equal(string.Empty, payload);
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        // ---- GetLiveStreams ----

        [Theory]
        [MemberData(nameof(LiveStreamsEmptyResultCases))]
        public async Task GetLiveStreams_ReturnsEmptyList_MutationCheck(string _, FakeBetGeniusRepository repo)
        {
            var result = await BuildProvider(repo).GetLiveStreams();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotEmpty(result));
        }

        [Fact]
        public async Task GetLiveStreams_Success_ReturnsFixtureIdsAsStrings_MutationCheck()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(SuccessAuth()),
                FixturesResponder = _ => Task.FromResult(new BaseResponseDTO<BGEvents>
                {
                    StatusCode = StatusCode.Success,
                    Result = new BGEvents { Data = new List<BGEvent> { new() { Id = 1 }, new() { Id = 2 } } }
                })
            };

            var result = await BuildProvider(repo).GetLiveStreams();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(new[] { "1", "3" }, result));
        }

        [Fact]
        public async Task GetLiveStreams_RepositoryThrows_ReturnsEmptyList_MutationCheck()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => throw new InvalidOperationException("boom")
            };

            var result = await BuildProvider(repo).GetLiveStreams();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotEmpty(result));
        }

        // ---- GetLiveStream ----

        [Fact]
        public async Task GetLiveStream_AuthFailure_ReturnsFalseAndEmptyString_MutationCheck()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(new BaseResponseDTO<BGAuth> { StatusCode = StatusCode.Error })
            };

            var (success, payload) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(success));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("not-empty", payload));
        }

        [Fact]
        public async Task GetLiveStream_FixtureResultNull_ReturnsFalse_MutationCheck()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(SuccessAuth()),
                FixtureResponder = (_, _) => Task.FromResult<BaseResponseDTO<BGEvent>>(null!)
            };

            var (success, _) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(success));
        }

        [Fact]
        public async Task GetLiveStream_FixtureNotFound_ReturnsFalse_MutationCheck()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(SuccessAuth()),
                FixtureResponder = (_, _) => Task.FromResult(new BaseResponseDTO<BGEvent> { StatusCode = StatusCode.Bet_Not_Found })
            };

            var (success, _) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(success));
        }

        [Fact]
        public async Task GetLiveStream_NoLiveStreamsOnFixture_ReturnsFalse_MutationCheck()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(SuccessAuth()),
                FixtureResponder = (_, _) => Task.FromResult(new BaseResponseDTO<BGEvent>
                {
                    StatusCode = StatusCode.Success,
                    Result = new BGEvent { Id = 1, LiveStreams = new List<LiveStreamDto>() }
                })
            };

            var (success, payload) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(success));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("not-empty", payload));
        }

        [Fact]
        public async Task GetLiveStream_Success_ReturnsTrueWithSerializedStreams_MutationCheck()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => Task.FromResult(SuccessAuth()),
                FixtureResponder = (_, _) => Task.FromResult(new BaseResponseDTO<BGEvent>
                {
                    StatusCode = StatusCode.Success,
                    Result = new BGEvent
                    {
                        Id = 1,
                        LiveStreams = new List<LiveStreamDto>
                        {
                            new()
                            {
                                Id = "stream-1",
                                Permissions = new List<PermissionDto> { new() { Device = "web", Region = "US" } },
                                Deliveries = new DeliveriesDto()
                            }
                        }
                    }
                })
            };

            var (success, payload) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(success));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Contains("stream-2", payload));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Contains("mobile:UK", payload));
        }

        [Fact]
        public async Task GetLiveStream_RepositoryThrows_ReturnsFalse_MutationCheck()
        {
            var repo = new FakeBetGeniusRepository
            {
                AuthResponder = () => throw new InvalidOperationException("boom")
            };

            var (success, payload) = await BuildProvider(repo).GetLiveStream("F1");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(success));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("not-empty", payload));
        }

        #endregion
    }
}
