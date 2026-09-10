using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;
using OE.WebSocket.Service.Clients;
using OE.WebSocket.Service.Repository;
using static OE.Websocket.Service.Tests.SportsbookRepositoryInternals;

namespace OE.Websocket.Service.Tests
{
    // The SignalR group list decides which browsers receive a payload. Getting it wrong shows up
    // as a fixture that stops updating for one page only, which is close to invisible in logs, so
    // the group strings are asserted literally. Uses the real MemCache with Guid-suffixed client
    // codes, matching SportsbookRepositoryProcessingTests.
    public class SportsbookRepositoryGroupTests
    {
        private const int SportId = 5;
        private const int SegmentId = 40;
        private const int LeagueId = 300;
        private const long FixtureId = 9001;

        private static OnMarketChangeMessage Message(int fiStId, int fiTyId = (int)FixtureType.Match, DateTime? startDate = null) =>
            new()
            {
                SpId = SportId,
                SegId = SegmentId,
                LeId = LeagueId,
                FiId = FixtureId,
                FiStId = fiStId,
                FiTyId = fiTyId,
                FiStDa = startDate ?? DateTime.UtcNow
            };

        private static OnBetStopFixtureMessage BetStop(int fiStId, int fiTyId = (int)FixtureType.Match) =>
            new()
            {
                SpId = SportId,
                SegId = SegmentId,
                LeId = LeagueId,
                FiId = FixtureId,
                FiStId = fiStId,
                FiTyId = fiTyId
            };

        // ---- GetGroupsForMainMarkets ----

        // A live fixture reaches the live lists. It reaches the today/daily lists too only in the
        // first two minutes after kick-off, which is the window in which those lists still show it.
        [Fact]
        public void MainMarkets_LiveFixture_JustKickedOff_AlsoReachesTodayAndDaily()
        {
            var code = NewClientCode();
            var repository = BuildRepository(new MemCache());

            var groups = Invoke<List<string>>(repository, "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Live, startDate: DateTime.UtcNow.AddSeconds(-30)));

            Assert.Equal(new[]
            {
                $"endpoint:{code}:sports:5:livefixtures",
                $"endpoint:{code}:sports:5:segments:40:livefixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:livefixtures",
                $"endpoint:{code}:sports:5:todayfixtures",
                $"endpoint:{code}:sports:5:dailyfixtures",
                $"endpoint:{code}:sports:5:segments:40:fixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:fixtures"
            }, groups);
        }

        [Fact]
        public void MainMarkets_LiveFixture_KickedOffLongAgo_StaysOffTodayAndDaily()
        {
            var code = NewClientCode();
            var repository = BuildRepository(new MemCache());

            var groups = Invoke<List<string>>(repository, "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Live, startDate: DateTime.UtcNow.AddMinutes(-5)));

            Assert.DoesNotContain($"endpoint:{code}:sports:5:todayfixtures", groups);
            Assert.DoesNotContain($"endpoint:{code}:sports:5:dailyfixtures", groups);
            Assert.Contains($"endpoint:{code}:sports:5:livefixtures", groups);
        }

        // A prematch match starting today is on the today and daily lists at all three levels.
        [Fact]
        public void MainMarkets_PrematchMatchToday_ReachesTodayAndDailyAtEveryLevel()
        {
            var code = NewClientCode();
            var repository = BuildRepository(new MemCache());

            var groups = Invoke<List<string>>(repository, "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Prematch, startDate: DateTime.UtcNow));

            Assert.Equal(new[]
            {
                $"endpoint:{code}:sports:5:todayfixtures",
                $"endpoint:{code}:sports:5:segments:40:todayfixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:todayfixtures",
                $"endpoint:{code}:sports:5:dailyfixtures",
                $"endpoint:{code}:sports:5:segments:40:dailyfixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:dailyfixtures",
                $"endpoint:{code}:sports:5:segments:40:fixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:fixtures"
            }, groups);
        }

        // A fixture starting tomorrow is on neither list: only the segment and league lists.
        [Fact]
        public void MainMarkets_PrematchMatchNotToday_ReachesOnlySegmentAndLeagueLists()
        {
            var code = NewClientCode();
            var repository = BuildRepository(new MemCache());

            var groups = Invoke<List<string>>(repository, "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Prematch, startDate: DateTime.UtcNow.AddDays(1)));

            Assert.Equal(new[]
            {
                $"endpoint:{code}:sports:5:segments:40:fixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:fixtures"
            }, groups);
        }

        [Fact]
        public void MainMarkets_PrematchOutright_ReachesTheOutrightList()
        {
            var code = NewClientCode();
            var repository = BuildRepository(new MemCache());

            var groups = Invoke<List<string>>(repository, "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Prematch, (int)FixtureType.Outright));

            Assert.Contains($"endpoint:{code}:sports:5:outrightfixtures", groups);
            Assert.DoesNotContain($"endpoint:{code}:sports:5:todayfixtures", groups);
        }

        // Neither prematch nor live (closed, ended, suspended...): the fixture is broadcast to
        // every list so clients can remove it wherever it is showing.
        [Fact]
        public void MainMarkets_ClosedMatch_ReachesEveryListSoClientsCanRemoveIt()
        {
            var code = NewClientCode();
            var repository = BuildRepository(new MemCache());

            var groups = Invoke<List<string>>(repository, "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Closed));

            Assert.Equal(new[]
            {
                $"endpoint:{code}:sports:5:livefixtures",
                $"endpoint:{code}:sports:5:segments:40:livefixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:livefixtures",
                $"endpoint:{code}:sports:5:todayfixtures",
                $"endpoint:{code}:sports:5:segments:40:todayfixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:todayfixtures",
                $"endpoint:{code}:sports:5:dailyfixtures",
                $"endpoint:{code}:sports:5:segments:40:dailyfixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:dailyfixtures",
                $"endpoint:{code}:sports:5:segments:40:fixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:fixtures"
            }, groups);
        }

        [Fact]
        public void MainMarkets_ClosedOutright_ReachesTheOutrightListOnly()
        {
            var code = NewClientCode();
            var repository = BuildRepository(new MemCache());

            var groups = Invoke<List<string>>(repository, "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Closed, (int)FixtureType.Outright));

            Assert.Equal(new[]
            {
                $"endpoint:{code}:sports:5:outrightfixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:fixtures"
            }, groups);
        }

        // ---- GetGroupsForMainMarkets: the cache-driven extras ----

        [Fact]
        public void MainMarkets_FixtureIsATopFixture_AddsTheTopFixturesGroup()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:TFL", new TopFixture { FiIds = { FixtureId } }, 1);

            var groups = Invoke<List<string>>(BuildRepository(memCache), "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Prematch, startDate: DateTime.UtcNow.AddDays(1)));

            Assert.Contains($"endpoint:{code}:topfixtures", groups);
        }

        [Fact]
        public void MainMarkets_FixtureIsNotATopFixture_LeavesTheGroupOut()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:TFL", new TopFixture { FiIds = { FixtureId + 1 } }, 1);

            var groups = Invoke<List<string>>(BuildRepository(memCache), "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Prematch, startDate: DateTime.UtcNow.AddDays(1)));

            Assert.DoesNotContain($"endpoint:{code}:topfixtures", groups);
        }

        // The top-league match is on the segment AND league pair, not on either alone.
        [Fact]
        public void MainMarkets_LeagueIsATopLeague_AddsTheTopLeagueGroup()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:TLL:{SportId}",
                new TopLeague { SegmentLeagueIds = { new Tuple<int, int>(SegmentId, LeagueId) } }, 1);

            var groups = Invoke<List<string>>(BuildRepository(memCache), "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Prematch, startDate: DateTime.UtcNow.AddDays(1)));

            Assert.Contains($"endpoint:{code}:sports:5:topleaguefixtures", groups);
        }

        [Fact]
        public void MainMarkets_SameLeagueIdInAnotherSegment_IsNotATopLeague()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:TLL:{SportId}",
                new TopLeague { SegmentLeagueIds = { new Tuple<int, int>(SegmentId + 1, LeagueId) } }, 1);

            var groups = Invoke<List<string>>(BuildRepository(memCache), "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Prematch, startDate: DateTime.UtcNow.AddDays(1)));

            Assert.DoesNotContain($"endpoint:{code}:sports:5:topleaguefixtures", groups);
        }

        // Favorites are per user and reach a private group, lowercased.
        [Fact]
        public void MainMarkets_LeagueAndFixtureFavorites_AddOnePrivateGroupPerUser()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:fli:{SportId}:{LeagueId}", new Favorites { UserIds = new List<string> { "USER-A" } }, 1);
            memCache.AddOrUpdateCache($"{code}:ffi:{SportId}:{FixtureId}", new Favorites { UserIds = new List<string> { "USER-B" } }, 1);

            var groups = Invoke<List<string>>(BuildRepository(memCache), "GetGroupsForMainMarkets",
                BuildClient(code), Message((int)FixtureStatus.Prematch, startDate: DateTime.UtcNow.AddDays(1)));

            Assert.Contains($"onfavorites:{code}:user-a", groups);
            Assert.Contains($"onfavorites:{code}:user-b", groups);
        }

        // ---- GetGroupsForAllMarkets ----

        [Fact]
        public void AllMarkets_AlwaysReachesFixtureDetailsAndTopMarkets()
        {
            var code = NewClientCode();

            var groups = Invoke<List<string>>(BuildRepository(new MemCache()), "GetGroupsForAllMarkets",
                BuildClient(code), Message((int)FixtureStatus.Live));

            Assert.Equal(new[]
            {
                $"endpoint:{code}:fixturedetails:9001",
                $"ontopmarketsoddschange:{code}:9001"
            }, groups);
        }

        [Fact]
        public void AllMarkets_BoostedFixture_AddsTheBoostedGroup()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:bf", new List<long> { FixtureId }, 1);

            var groups = Invoke<List<string>>(BuildRepository(memCache), "GetGroupsForAllMarkets",
                BuildClient(code), Message((int)FixtureStatus.Live));

            Assert.Contains($"endpoint:{code}:boostedfixtures", groups);
        }

        [Fact]
        public void AllMarkets_OtherFixtureBoosted_LeavesTheBoostedGroupOut()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:bf", new List<long> { FixtureId + 1 }, 1);

            var groups = Invoke<List<string>>(BuildRepository(memCache), "GetGroupsForAllMarkets",
                BuildClient(code), Message((int)FixtureStatus.Live));

            Assert.DoesNotContain($"endpoint:{code}:boostedfixtures", groups);
        }

        // ---- GetGroups (bet-stop on one fixture) ----

        // Unlike the odds-change builder, the bet-stop builder does not gate today/daily on the
        // start date: a prematch match always reaches them. The date checks are commented out in
        // the production code, so this pins current behaviour, not an intent.
        [Fact]
        public void BetStop_PrematchMatch_ReachesTodayAndDailyRegardlessOfStartDate()
        {
            var code = NewClientCode();

            var groups = Invoke<List<string>>(BuildRepository(new MemCache()), "GetGroups",
                BuildClient(code), BetStop((int)FixtureStatus.Prematch));

            Assert.Equal(new[]
            {
                $"endpoint:{code}:fixturedetails:9001",
                $"endpoint:{code}:sports:5:todayfixtures",
                $"endpoint:{code}:sports:5:segments:40:todayfixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:todayfixtures",
                $"endpoint:{code}:sports:5:dailyfixtures",
                $"endpoint:{code}:sports:5:segments:40:dailyfixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:dailyfixtures",
                $"endpoint:{code}:sports:5:segments:40:fixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:fixtures"
            }, groups);
        }

        [Fact]
        public void BetStop_LiveFixture_ReachesTheLiveListsOnly()
        {
            var code = NewClientCode();

            var groups = Invoke<List<string>>(BuildRepository(new MemCache()), "GetGroups",
                BuildClient(code), BetStop((int)FixtureStatus.Live));

            Assert.Equal(new[]
            {
                $"endpoint:{code}:fixturedetails:9001",
                $"endpoint:{code}:sports:5:livefixtures",
                $"endpoint:{code}:sports:5:segments:40:livefixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:livefixtures",
                $"endpoint:{code}:sports:5:segments:40:fixtures",
                $"endpoint:{code}:sports:5:segments:40:leagues:300:fixtures"
            }, groups);
        }

        [Fact]
        public void BetStop_PrematchOutright_ReachesTheOutrightList()
        {
            var code = NewClientCode();

            var groups = Invoke<List<string>>(BuildRepository(new MemCache()), "GetGroups",
                BuildClient(code), BetStop((int)FixtureStatus.Prematch, (int)FixtureType.Outright));

            Assert.Contains($"endpoint:{code}:sports:5:outrightfixtures", groups);
            Assert.DoesNotContain($"endpoint:{code}:sports:5:todayfixtures", groups);
        }

        [Fact]
        public void BetStop_TopLeagueTopFixtureFavoritesAndBoosted_AllAddTheirGroups()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:TLL:{SportId}",
                new TopLeague { SegmentLeagueIds = { new Tuple<int, int>(SegmentId, LeagueId) } }, 1);
            memCache.AddOrUpdateCache($"{code}:TFL", new TopFixture { FiIds = { FixtureId } }, 1);
            memCache.AddOrUpdateCache($"{code}:fli:{SportId}:{LeagueId}", new Favorites { UserIds = new List<string> { "USER-A" } }, 1);
            memCache.AddOrUpdateCache($"{code}:ffi:{SportId}:{FixtureId}", new Favorites { UserIds = new List<string> { "USER-B" } }, 1);
            memCache.AddOrUpdateCache($"{code}:bf", new List<long> { FixtureId }, 1);

            var groups = Invoke<List<string>>(BuildRepository(memCache), "GetGroups",
                BuildClient(code), BetStop((int)FixtureStatus.Live));

            Assert.Contains($"endpoint:{code}:sports:5:topleaguefixtures", groups);
            Assert.Contains($"endpoint:{code}:topfixtures", groups);
            Assert.Contains($"onfavorites:{code}:user-a", groups);
            Assert.Contains($"onfavorites:{code}:user-b", groups);
            Assert.Contains($"endpoint:{code}:boostedfixtures", groups);
        }

        // ---- The per-fixture numbers that ride on the payload ----

        [Fact]
        public void GetMarketCount_NoCacheEntry_IsZero()
        {
            var code = NewClientCode();

            Assert.Equal(0, Invoke<int>(BuildRepository(new MemCache()), "GetMarketCount", BuildClient(code), FixtureId));
        }

        [Fact]
        public void GetMarketCount_CountsTheCachedMarketIds()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:mc:{FixtureId}", new MarketCount { MarketIds = { 1, 2, 3 } }, 1);

            Assert.Equal(3, Invoke<int>(BuildRepository(memCache), "GetMarketCount", BuildClient(code), FixtureId));
        }

        // 1000 is the "unsorted, show last" sentinel the platform expects.
        [Fact]
        public void GetFixtureSortOrder_NoCacheEntry_IsTheUnsortedSentinel()
        {
            var code = NewClientCode();

            Assert.Equal(1000, Invoke<int>(BuildRepository(new MemCache()), "GetFixtureSortOrder",
                BuildClient(code), SportId, SegmentId, LeagueId));
        }

        [Fact]
        public void GetFixtureSortOrder_UsesTheCachedOrderForThatSportSegmentLeague()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:fso:{SportId}:{SegmentId}:{LeagueId}", new FixtureSortOrder { SortOrder = 7 }, 1);

            Assert.Equal(7, Invoke<int>(BuildRepository(memCache), "GetFixtureSortOrder",
                BuildClient(code), SportId, SegmentId, LeagueId));
        }

        // ---- GetMarketSortOrder ----

        // Configured templates keep their configured order (one-based); anything not configured is
        // pushed past them by its own template id plus 1000.
        [Fact]
        public void GetMarketSortOrder_Prematch_RanksConfiguredTemplatesFirst()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:pmso:{SportId}", new ClientMarketSortOrder { MaTeIds = { "16", "18" } }, 1);

            var markets = new List<MarketMessage>
            {
                new() { MaTeId = "18" },
                new() { MaTeId = "16" },
                new() { MaTeId = "77" }
            };

            var result = Invoke<List<Tuple<string, int>>>(BuildRepository(memCache), "GetMarketSortOrder",
                BuildClient(code), markets, SportId, (int)FixtureStatus.Prematch);

            Assert.Equal(new[]
            {
                new Tuple<string, int>("18", 2),
                new Tuple<string, int>("16", 1),
                new Tuple<string, int>("77", 1077)
            }, result);
        }

        // Live fixtures read a different cache key, so the prematch order must not apply to them.
        [Fact]
        public void GetMarketSortOrder_Live_ReadsTheLiveCacheKey()
        {
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:pmso:{SportId}", new ClientMarketSortOrder { MaTeIds = { "16" } }, 1);
            memCache.AddOrUpdateCache($"{code}:lmso:{SportId}", new ClientMarketSortOrder { MaTeIds = { "77" } }, 1);

            var markets = new List<MarketMessage> { new() { MaTeId = "16" }, new() { MaTeId = "77" } };

            var result = Invoke<List<Tuple<string, int>>>(BuildRepository(memCache), "GetMarketSortOrder",
                BuildClient(code), markets, SportId, (int)FixtureStatus.Live);

            Assert.Equal(new[]
            {
                new Tuple<string, int>("16", 1016),
                new Tuple<string, int>("77", 1)
            }, result);
        }

        [Fact]
        public void GetMarketSortOrder_NoMarkets_ReturnsNothing()
        {
            var code = NewClientCode();

            var result = Invoke<List<Tuple<string, int>>>(BuildRepository(new MemCache()), "GetMarketSortOrder",
                BuildClient(code), new List<MarketMessage>(), SportId, (int)FixtureStatus.Prematch);

            Assert.Empty(result);
        }

        [Fact]
        public void GetMarketSortOrder_NoCachedOrder_ReturnsNothing()
        {
            var code = NewClientCode();

            var result = Invoke<List<Tuple<string, int>>>(BuildRepository(new MemCache()), "GetMarketSortOrder",
                BuildClient(code), new List<MarketMessage> { new() { MaTeId = "16" } }, SportId, (int)FixtureStatus.Prematch);

            Assert.Empty(result);
        }

        // ---- PrepareSelectionName ----

        // Handicap markets 16 and 66 get the line appended to the selection name, with the sign
        // flipped for the away template (1715).
        [Theory]
        [InlineData("16", "1714", "1.5", "Home (+1.5)")]
        [InlineData("16", "1714", "-1.5", "Home (-1.5)")]
        [InlineData("66", "1715", "1.5", "Home (-1.5)")]
        [InlineData("66", "1715", "-1.5", "Home (+1.5)")]
        public void PrepareSelectionName_HandicapMarket_AppendsTheSignedLine(string maTeId, string seTeId, string line, string expected)
        {
            Assert.Equal(expected, Invoke<string>(BuildRepository(new MemCache()), "PrepareSelectionName",
                maTeId, seTeId, "Home", line));
        }

        [Fact]
        public void PrepareSelectionName_ZeroLine_AppendsPlainZeroForEitherTemplate()
        {
            var repository = BuildRepository(new MemCache());

            Assert.Equal("Home (0)", Invoke<string>(repository, "PrepareSelectionName", "16", "1714", "Home", "0"));
            Assert.Equal("Home (0)", Invoke<string>(repository, "PrepareSelectionName", "16", "1715", "Home", "0"));
        }

        [Fact]
        public void PrepareSelectionName_UnknownSelectionTemplate_LeavesTheNameAlone()
        {
            Assert.Equal("Home", Invoke<string>(BuildRepository(new MemCache()), "PrepareSelectionName",
                "16", "9999", "Home", "1.5"));
        }

        [Fact]
        public void PrepareSelectionName_NonHandicapMarket_LeavesTheNameAlone()
        {
            Assert.Equal("Home", Invoke<string>(BuildRepository(new MemCache()), "PrepareSelectionName",
                "18", "1714", "Home", "1.5"));
        }

        // ---- GetHomeName / GetAwayName ----

        [Fact]
        public void HomeAndAwayName_MatchFixture_SplitOnTheVersusSeparator()
        {
            Assert.Equal("Arsenal", InvokeStatic<string>("GetHomeName", "Arsenal vs. Chelsea", FixtureType.Match));
            Assert.Equal("Chelsea", InvokeStatic<string>("GetAwayName", "Arsenal vs. Chelsea", FixtureType.Match));
        }

        // An outright has no two sides, so both names are empty rather than the whole title.
        [Fact]
        public void HomeAndAwayName_Outright_AreEmpty()
        {
            Assert.Equal(string.Empty, InvokeStatic<string>("GetHomeName", "Premier League Winner", FixtureType.Outright));
            Assert.Equal(string.Empty, InvokeStatic<string>("GetAwayName", "Premier League Winner", FixtureType.Outright));
        }
    }
}
