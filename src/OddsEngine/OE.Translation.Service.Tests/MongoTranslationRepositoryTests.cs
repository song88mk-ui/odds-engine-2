using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Translation.Service.Repositories;

namespace OE.Translation.Service.Tests
{
    public class MongoTranslationRepositoryTests
    {
        // Moved from OE.Sportsbook.Service.Tests together with the change path: the translation-only
        // ON_ODDS_CHANGE payload is synthesized from the Mongo fixture document.
        [Fact]
        public void ToFixtureChangeMessage_MapsTheFieldsTheWebsocketPathNeeds()
        {
            var fixture = new Fixture
            {
                Id = "0:1234567890",
                ChTyId = 0,
                PrFiId = "1234567890",
                FiId = 42,
                SpId = 1,
                SpNa = "Soccer",
                SegId = 2,
                SegNa = "England",
                LeId = 3,
                LeNa = "Premier League",
                FiNa = "Arsenal vs. Chelsea",
                FiStId = 1,
                FiSoOr = 7,
                HoId = "H1",
                HoNa = "Arsenal",
                AwId = "A1",
                AwNa = "Chelsea",
                ClCo = "CL1"
            };

            var message = MongoTranslationRepository.ToFixtureChangeMessage(fixture);

            Assert.Equal("0:1234567890", message.Id); // recomputed from ChTyId + PrFiId
            Assert.Equal(42, message.FiId);
            Assert.Equal(1, message.SpId);
            Assert.Equal("Arsenal", message.HoNa);
            Assert.Equal("Chelsea", message.AwNa);
            Assert.Equal(7, message.SoOr); // FiSoOr -> SoOr
            Assert.Equal("None", message.Fa);
        }

        #region ApplyTranslationDeltas

        private static Fixture MakeFixture(int spId = 1, int segId = 2, int leId = 3, string hoId = "H1", string awId = "A1")
        {
            return new Fixture { SpId = spId, SegId = segId, LeId = leId, HoId = hoId, AwId = awId };
        }

        private static OnInternalOddsChangeMessage MakePayload()
        {
            return new OnInternalOddsChangeMessage
            {
                Ma = new List<InternalMarketMessage>
                {
                    new InternalMarketMessage
                    {
                        MaTeId = "MT1",
                        Se = new List<InternalSelectionMessage>
                        {
                            new InternalSelectionMessage { MaTeId = "MT1", SeTeId = "ST1" }
                        }
                    }
                }
            };
        }

        private static List<TranslationMessage> Delta(string text) =>
            new List<TranslationMessage> { new TranslationMessage { LaCo = "ZH", Tx = text } };

        // Minimal ITranslationCache stub — keyed lookup only, no preload/Apply behavior needed
        // for these pure ApplyTranslationDeltas tests.
        private class StubTranslationCache : ITranslationCache
        {
            private readonly Dictionary<string, List<TranslationMessage>> _byKey;
            public StubTranslationCache(Dictionary<string, List<TranslationMessage>> byKey) => _byKey = byKey;
            public void Preload() { }
            public List<TranslationMessage>? GetTranslations(string stringId, string currentText) =>
                _byKey.TryGetValue(stringId, out var v) ? v : null;
            public void Apply(List<TranslationChangeItem> items) { }
        }

        [Fact]
        public void ApplyTranslationDeltas_HomeOnlyDelta_SetsHoTrAndLeavesAwTrNull()
        {
            var fixture = MakeFixture(hoId: "H1", awId: "A1");
            var payload = MakePayload();
            var teamDeltas = new Dictionary<string, List<TranslationMessage>> { ["H1"] = Delta("主队") };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(),
                teamDeltas: teamDeltas,
                marketTemplateDeltas: new(), selectionTemplateDeltas: new());

            Assert.Same(teamDeltas["H1"], payload.HoTr);
            Assert.Null(payload.AwTr);
            Assert.Null(payload.SpTr);
            Assert.Null(payload.SegTr);
            Assert.Null(payload.LeTr);
        }

        [Fact]
        public void ApplyTranslationDeltas_AwayOnlyDelta_SetsAwTrAndLeavesHoTrNull()
        {
            var fixture = MakeFixture(hoId: "H1", awId: "A1");
            var payload = MakePayload();
            var teamDeltas = new Dictionary<string, List<TranslationMessage>> { ["A1"] = Delta("客队") };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(),
                teamDeltas: teamDeltas,
                marketTemplateDeltas: new(), selectionTemplateDeltas: new());

            Assert.Null(payload.HoTr);
            Assert.Same(teamDeltas["A1"], payload.AwTr);
        }

        [Fact]
        public void ApplyTranslationDeltas_LeagueDeltaForDifferentFixture_LeavesLeTrNull()
        {
            var fixture = MakeFixture(leId: 3);
            var payload = MakePayload();
            var leagueDeltas = new Dictionary<int, List<TranslationMessage>> { [999] = Delta("Some League") };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: leagueDeltas,
                teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: new());

            Assert.Null(payload.LeTr);
        }

        [Fact]
        public void ApplyTranslationDeltas_MarketTemplateDelta_SetsMaTeTrOnMatchingMarketOnly()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            payload.Ma.Add(new InternalMarketMessage { MaTeId = "MT2", Se = new List<InternalSelectionMessage>() });
            var marketTemplateDeltas = new Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>>
            {
                ["MT1"] = new() { (null, Delta("Over/Under")) }
            };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: marketTemplateDeltas,
                selectionTemplateDeltas: new());

            Assert.Same(marketTemplateDeltas["MT1"][0].Deltas, payload.Ma[0].MaTeTr);
            Assert.Null(payload.Ma[1].MaTeTr);
        }

        // 2026-07-16: the feed emits concurrent wordings per template id; a wording-variant
        // announce (SrcTx set) applies only to the markets whose MaNa carries that wording,
        // while an unscoped announce (SrcTx null, older senders) applies template-wide.
        [Fact]
        public void ApplyTranslationDeltas_WordingScopedMarketDelta_AppliesOnlyToMatchingMaNa()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            payload.Ma[0].MaNa = "Over/Under";
            payload.Ma.Add(new InternalMarketMessage { MaTeId = "MT1", MaNa = "Total Goals Over / Under", Se = new List<InternalSelectionMessage>() });

            var scoped = Delta("大/小");
            var marketTemplateDeltas = new Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>>
            {
                ["MT1"] = new() { ("Over/Under", scoped) }
            };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: marketTemplateDeltas,
                selectionTemplateDeltas: new());

            Assert.Same(scoped, payload.Ma[0].MaTeTr);  // MaNa matches the wording
            Assert.Null(payload.Ma[1].MaTeTr);          // other wording untouched
        }

        // An exact-wording entry wins over an unscoped one for the matching market; the unscoped
        // entry still covers everything else.
        [Fact]
        public void ResolveMarketTemplateDeltas_ExactWordingBeatsUnscoped()
        {
            var scoped = Delta("大/小");
            var unscoped = Delta("总进球数 大/小于");
            var deltas = new Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>>
            {
                ["MT1"] = new() { (null, unscoped), ("Over/Under", scoped) }
            };

            Assert.Same(scoped, MongoTranslationRepository.ResolveMarketTemplateDeltas(deltas, "MT1", "Over/Under"));
            Assert.Same(scoped, MongoTranslationRepository.ResolveMarketTemplateDeltas(deltas, "MT1", "over/under")); // case drift tolerated
            Assert.Same(unscoped, MongoTranslationRepository.ResolveMarketTemplateDeltas(deltas, "MT1", "Total Goals Over / Under"));
            Assert.Null(MongoTranslationRepository.ResolveMarketTemplateDeltas(deltas, "MT2", "Over/Under"));
        }

        #region Change path: SrcTx grouping and MaTe market scoping (GM-852, GM-874)

        // ProcessTranslationChange itself needs a live Mongo, so these cover the pure pieces it is
        // built from: how the announce is grouped, which Mongo filter a MaTe update runs with, and
        // which deltas the push path then picks per market.

        private static BsonDocument Render(FilterDefinition<Market> filter) =>
            filter.Render(new RenderArgs<Market>(BsonSerializer.SerializerRegistry.GetSerializer<Market>(), BsonSerializer.SerializerRegistry));

        private static TranslationChangeItem Item(string stId, string laCo, string? tx, string? srcTx) =>
            new TranslationChangeItem { StId = stId, LaCo = laCo, Tx = tx!, SrcTx = srcTx! };

        [Fact]
        public void GroupDeltas_KeepsSrcTxPerKeyAndGroupsLanguages()
        {
            var (deltasByKey, sourceTextByKey) = MongoTranslationRepository.GroupDeltas(new List<TranslationChangeItem>
            {
                Item("MaTe:16", "zh", "总进球", "Total Goals"),
                Item("MaTe:16", "ES", "Goles totales", "Total Goals"),
                Item("Te:2", "FR", null, null)
            });

            Assert.Equal(new[] { "ZH", "ES" }, deltasByKey["MaTe:16"].Select(d => d.LaCo)); // upper-cased
            Assert.Equal(string.Empty, Assert.Single(deltasByKey["Te:2"]).Tx); // null Tx is a removal
            Assert.Equal("Total Goals", sourceTextByKey["MaTe:16"]);
            Assert.False(sourceTextByKey.ContainsKey("Te:2")); // no SrcTx: unscoped, not an empty wording
        }

        [Fact]
        public void GroupDeltas_SkipsItemsWithoutKeyOrLanguage()
        {
            var (deltasByKey, sourceTextByKey) = MongoTranslationRepository.GroupDeltas(new List<TranslationChangeItem>
            {
                null!,
                Item("", "ZH", "x", "Total Goals"),
                Item("MaTe:16", "", "x", "Total Goals")
            });

            Assert.Empty(deltasByKey);
            Assert.Empty(sourceTextByKey);
        }

        [Fact]
        public void BuildMaTeMarketFilter_WithSrcTx_AddsAnchoredEscapedCaseInsensitiveMaNaRegex()
        {
            var rendered = Render(MongoTranslationRepository.BuildMaTeMarketFilter(new[] { 42L }, new[] { 1, 2 }, "259", "Over/Under (1.5)"));

            var regex = rendered["MaNa"].AsBsonRegularExpression;
            Assert.Equal(@"^Over/Under\ \(1\.5\)$", regex.Pattern);
            Assert.Equal("i", regex.Options);

            var asDotNet = new Regex(regex.Pattern, RegexOptions.IgnoreCase);
            Assert.Matches(asDotNet, "over/under (1.5)"); // casing drift between TC2 and the feed
            Assert.DoesNotMatch(asDotNet, "Over/Under (1x5)"); // "." escaped
            Assert.DoesNotMatch(asDotNet, "1st Half Over/Under (1.5)"); // anchored: exact wording only
            Assert.DoesNotMatch(asDotNet, "Over/Under (1.5) Corners");
        }

        [Fact]
        public void BuildMaTeMarketFilter_KeepsOpenFixtureStatusTemplateAndRegularMarketScope()
        {
            var rendered = Render(MongoTranslationRepository.BuildMaTeMarketFilter(new[] { 42L, 43L }, new[] { 1, 2 }, "259", "Asian Handicap"));

            Assert.Equal(new BsonArray { 42L, 43L }, rendered["FiId"]["$in"].AsBsonArray);
            Assert.Equal(new BsonArray { 1, 2 }, rendered["MaStId"]["$in"].AsBsonArray);
            Assert.Equal("259", rendered["MaTeId"].AsString);
            Assert.Equal(BsonNull.Value, rendered["CompId"]); // player markets are recomposed, not overwritten
        }

        // Older senders omit SrcTx: the update stays template-wide.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void BuildMaTeMarketFilter_WithoutSrcTx_HasNoMaNaClause(string? srcTx)
        {
            var rendered = Render(MongoTranslationRepository.BuildMaTeMarketFilter(new[] { 42L }, new[] { 1 }, "259", srcTx!));

            Assert.False(rendered.Contains("MaNa"));
            Assert.Equal("259", rendered["MaTeId"].AsString);
        }

        [Fact]
        public void AddMarketTemplateDelta_VariantId_StoresUnderBaseIdWithSrcTx()
        {
            var marketTemplateDeltas = new Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>>();
            var deltas = Delta("让球");

            var baseId = MongoTranslationRepository.AddMarketTemplateDelta(marketTemplateDeltas, "259~a1b2c3d4", "Asian Handicap", deltas);

            Assert.Equal("259", baseId);
            var entry = Assert.Single(marketTemplateDeltas["259"]);
            Assert.Equal("Asian Handicap", entry.SrcTx);
            Assert.Same(deltas, entry.Deltas);
        }

        // The whole change path end to end without Mongo: announce, group, record, then resolve per
        // market. A variant announce reaches only the market carrying its wording.
        [Fact]
        public void ChangePath_VariantAnnounce_ReachesOnlyTheMarketWithThatWording()
        {
            var (deltasByKey, sourceTextByKey) = MongoTranslationRepository.GroupDeltas(new List<TranslationChangeItem>
            {
                Item("MaTe:259~a1b2c3d4", "ZH", "亚洲让球", "Asian Handicap")
            });
            var marketTemplateDeltas = new Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>>();
            foreach (var entry in deltasByKey)
            {
                sourceTextByKey.TryGetValue(entry.Key, out var srcTx);
                MongoTranslationRepository.AddMarketTemplateDelta(marketTemplateDeltas, entry.Key.Substring("MaTe:".Length), srcTx!, entry.Value);
            }

            var matching = MongoTranslationRepository.ResolveMarketTemplateDeltas(marketTemplateDeltas, "259", "asian handicap");
            Assert.Equal("亚洲让球", Assert.Single(matching).Tx);
            Assert.Null(MongoTranslationRepository.ResolveMarketTemplateDeltas(marketTemplateDeltas, "259", "Asian Handicap 1st Half"));
        }

        // Behaviour change from GM-852: a BASE announce now carries SrcTx too, so it is scoped to its
        // own wording. A market of the same template with another wording no longer receives it
        // until that wording has its own variant row.
        [Fact]
        public void ChangePath_BaseAnnounceWithSrcTx_IsScopedToItsWording()
        {
            var (deltasByKey, sourceTextByKey) = MongoTranslationRepository.GroupDeltas(new List<TranslationChangeItem>
            {
                Item("MaTe:16", "ZH", "总进球", "Total Goals")
            });
            var marketTemplateDeltas = new Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>>();
            sourceTextByKey.TryGetValue("MaTe:16", out var srcTx);
            var baseId = MongoTranslationRepository.AddMarketTemplateDelta(marketTemplateDeltas, "16", srcTx!, deltasByKey["MaTe:16"]);

            Assert.True(Render(MongoTranslationRepository.BuildMaTeMarketFilter(new[] { 42L }, new[] { 1 }, baseId, srcTx!)).Contains("MaNa"));
            Assert.NotNull(MongoTranslationRepository.ResolveMarketTemplateDeltas(marketTemplateDeltas, "16", "Total Goals"));
            Assert.Null(MongoTranslationRepository.ResolveMarketTemplateDeltas(marketTemplateDeltas, "16", "Total Goals Odd/Even"));
        }

        #endregion

        [Fact]
        public void ApplyTranslationDeltas_SelectionTemplateDelta_MatchesByMarketAndSelectionTemplateIdPair()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            // Same SeTeId under a different MaTeId must NOT match — the key is the (MaTeId, SeTeId) pair.
            var selectionTemplateDeltas = new Dictionary<(string, string), List<TranslationMessage>>
            {
                [("MT-OTHER", "ST1")] = Delta("Over"),
                [("MT1", "ST1")] = Delta("大于")
            };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(),
                selectionTemplateDeltas: selectionTemplateDeltas);

            Assert.Same(selectionTemplateDeltas[("MT1", "ST1")], payload.Ma[0].Se[0].SeNaTr);
        }

        // Co:/SeTe: are mutually exclusive lookup keys for the same SeNaTr result (2026-07-09):
        // a competitor-specific selection (CompId != null) resolves against competitorDeltas,
        // never selectionTemplateDeltas.
        [Fact]
        public void ApplyTranslationDeltas_CompetitorDelta_SetsSeNaTrFromCompetitorDeltas()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            payload.Ma[0].Se[0].CompId = 810068;
            var competitorDeltas = new Dictionary<int, List<TranslationMessage>> { [810068] = Delta("沙瓦兹曼") };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: new(),
                competitorDeltas: competitorDeltas);

            Assert.Same(competitorDeltas[810068], payload.Ma[0].Se[0].SeNaTr);
        }

        [Fact]
        public void ApplyTranslationDeltas_NonCompetitorSelection_IgnoresCompetitorDeltasAndUsesSeTe()
        {
            var fixture = MakeFixture();
            var payload = MakePayload(); // Se[0].CompId is null
            var competitorDeltas = new Dictionary<int, List<TranslationMessage>> { [999] = Delta("Unrelated") };
            var selectionTemplateDeltas = new Dictionary<(string, string), List<TranslationMessage>> { [("MT1", "ST1")] = Delta("大于") };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: selectionTemplateDeltas,
                competitorDeltas: competitorDeltas);

            Assert.Same(selectionTemplateDeltas[("MT1", "ST1")], payload.Ma[0].Se[0].SeNaTr);
        }

        // 2026-07-15: positional token selections ("Home or Draw") carry a COMPOSED SeNaTr that
        // embeds the fixture's translated team names (see SelectionTranslationComposer) — a SeTe:
        // template edit must therefore compose the delta per fixture, not forward raw template
        // text. The English SeTeNa comes in via seTeNaByTemplate (the payload doesn't carry it).
        [Fact]
        public void ApplyTranslationDeltas_TokenTemplateDelta_ComposesFixtureTeamNamesOverTheDelta()
        {
            var fixture = MakeFixture(hoId: "H1", awId: "A1");
            fixture.HoNa = "Korona Kielce";
            fixture.AwNa = "Stal Rzeszów";
            var payload = MakePayload();
            var selectionTemplateDeltas = new Dictionary<(string, string), List<TranslationMessage>> { [("MT1", "ST1")] = Delta("主队胜或平局") };
            var translationCache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["Te:H1"] = Delta("凯尔采"),
                ["Te:A1"] = Delta("斯塔尔热舒夫")
            });
            var seTeNaByTemplate = new Dictionary<(string MaTeId, string SeTeId), string> { [("MT1", "ST1")] = "Home or Draw" };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: selectionTemplateDeltas,
                translationCache: translationCache,
                seTeNaByTemplate: seTeNaByTemplate);

            Assert.Equal("凯尔采胜或平局", payload.Ma[0].Se[0].SeNaTr!.Single().Tx);
        }

        // A TEAM-name edit must refresh token selections too: the template itself didn't change,
        // so the template text is re-read from the (already Apply'd) cache and recomposed with
        // the new team translation.
        [Fact]
        public void ApplyTranslationDeltas_TeamDeltaOnly_RecomposesTokenSelectionFromCachedTemplate()
        {
            var fixture = MakeFixture(hoId: "H1", awId: "A1");
            fixture.HoNa = "Korona Kielce";
            var payload = MakePayload();
            var teamDeltas = new Dictionary<string, List<TranslationMessage>> { ["H1"] = Delta("新凯尔采") };
            var translationCache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["SeTe:MT1:ST1"] = Delta("主队胜或平局"),
                ["Te:H1"] = Delta("新凯尔采")
            });
            var seTeNaByTemplate = new Dictionary<(string MaTeId, string SeTeId), string> { [("MT1", "ST1")] = "Home or Draw" };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(),
                teamDeltas: teamDeltas,
                marketTemplateDeltas: new(), selectionTemplateDeltas: new(),
                translationCache: translationCache,
                seTeNaByTemplate: seTeNaByTemplate);

            Assert.Equal("新凯尔采胜或平局", payload.Ma[0].Se[0].SeNaTr!.Single().Tx);
        }

        // A push about neither this template nor this fixture's teams leaves the token selection
        // null — consumers keep whatever they already display.
        [Fact]
        public void ApplyTranslationDeltas_TokenSelectionWithUnrelatedPush_LeavesSeNaTrNull()
        {
            var fixture = MakeFixture(hoId: "H1", awId: "A1");
            var payload = MakePayload();
            var teamDeltas = new Dictionary<string, List<TranslationMessage>> { ["UNRELATED-TEAM"] = Delta("别队") };
            var translationCache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["SeTe:MT1:ST1"] = Delta("主队胜或平局")
            });
            var seTeNaByTemplate = new Dictionary<(string MaTeId, string SeTeId), string> { [("MT1", "ST1")] = "Home or Draw" };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(),
                teamDeltas: teamDeltas,
                marketTemplateDeltas: new(), selectionTemplateDeltas: new(),
                translationCache: translationCache,
                seTeNaByTemplate: seTeNaByTemplate);

            Assert.Null(payload.Ma[0].Se[0].SeNaTr);
        }

        // Backward compatibility: without seTeNaByTemplate the token trigger can't fire and the
        // raw selectionTemplateDeltas assignment applies — the pre-compose behavior.
        [Fact]
        public void ApplyTranslationDeltas_NoSeTeNaMapSupplied_FallsBackToRawTemplateDelta()
        {
            var fixture = MakeFixture(hoId: "H1", awId: "A1");
            var payload = MakePayload();
            var selectionTemplateDeltas = new Dictionary<(string, string), List<TranslationMessage>> { [("MT1", "ST1")] = Delta("主队胜或平局") };
            var translationCache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["Te:H1"] = Delta("凯尔采")
            });

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: selectionTemplateDeltas,
                translationCache: translationCache);

            Assert.Same(selectionTemplateDeltas[("MT1", "ST1")], payload.Ma[0].Se[0].SeNaTr);
        }

        // A competitor-name selection (e.g. Match Winner, where the selections ARE the two teams)
        // has CompId == the fixture's own HoId/AwId, which SelectionTranslationKeyResolver redirects
        // to Te:{HoId}/{AwId} instead of Co:{CompId} — this was the gap that left the selection's
        // displayed name stale after a BO team-name edit even though Fixture.HoNaTr/AwNaTr updated.
        [Fact]
        public void ApplyTranslationDeltas_CompIdMatchesHomeParticipant_ResolvesViaTeKeyInsteadOfCompetitorDeltas()
        {
            var fixture = MakeFixture(hoId: "12345", awId: "999");
            fixture.HoNa = "Werewolves CC";
            var payload = MakePayload();
            payload.Ma[0].Se[0].CompId = 12345;
            payload.Ma[0].Se[0].SeNa = "Werewolves CC";
            var teamDeltas = new Dictionary<string, List<TranslationMessage>> { ["12345"] = Delta("主队改名") };
            var competitorDeltas = new Dictionary<int, List<TranslationMessage>> { [12345] = Delta("WRONG — should not be used") };
            var translationCache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["Te:12345"] = Delta("狼人 CC")
            });

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(),
                teamDeltas: teamDeltas,
                marketTemplateDeltas: new(), selectionTemplateDeltas: new(),
                competitorDeltas: competitorDeltas,
                translationCache: translationCache);

            Assert.Same(translationCache.GetTranslations("Te:12345", "Werewolves CC"), payload.Ma[0].Se[0].SeNaTr);
        }

        // If this specific push isn't about the participant's team (teamDeltas has no entry for it),
        // the selection must fall back to the ordinary Co:/SeTe: path rather than attempting a Te:
        // resolution — avoids resolving/looking up a key that has no delta on this message.
        [Fact]
        public void ApplyTranslationDeltas_CompIdMatchesParticipantButNoTeamDelta_FallsBackToCompetitorDeltas()
        {
            var fixture = MakeFixture(hoId: "12345", awId: "999");
            var payload = MakePayload();
            payload.Ma[0].Se[0].CompId = 12345;
            var competitorDeltas = new Dictionary<int, List<TranslationMessage>> { [12345] = Delta("沙瓦兹曼") };
            var translationCache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["Te:12345"] = Delta("should not be reached")
            });

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(),
                teamDeltas: new(), // no entry for "12345" — this push isn't a team-name change
                marketTemplateDeltas: new(), selectionTemplateDeltas: new(),
                competitorDeltas: competitorDeltas,
                translationCache: translationCache);

            Assert.Same(competitorDeltas[12345], payload.Ma[0].Se[0].SeNaTr);
        }

        // Backward compatibility: callers that don't supply a translationCache (the default null)
        // must keep the pre-existing Co:/SeTe: behavior instead of throwing or silently no-op'ing.
        [Fact]
        public void ApplyTranslationDeltas_NoTranslationCacheSupplied_FallsBackToCompetitorDeltas()
        {
            var fixture = MakeFixture(hoId: "12345", awId: "999");
            var payload = MakePayload();
            payload.Ma[0].Se[0].CompId = 12345;
            var teamDeltas = new Dictionary<string, List<TranslationMessage>> { ["12345"] = Delta("主队改名") };
            var competitorDeltas = new Dictionary<int, List<TranslationMessage>> { [12345] = Delta("沙瓦兹曼") };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(),
                teamDeltas: teamDeltas,
                marketTemplateDeltas: new(), selectionTemplateDeltas: new(),
                competitorDeltas: competitorDeltas);
            // translationCache omitted entirely — defaults to null

            Assert.Same(competitorDeltas[12345], payload.Ma[0].Se[0].SeNaTr);
        }

        [Fact]
        public void ApplyTranslationDeltas_NoMatchingDeltas_LeavesEverythingNull()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: new());

            Assert.Null(payload.SpTr);
            Assert.Null(payload.SegTr);
            Assert.Null(payload.LeTr);
            Assert.Null(payload.HoTr);
            Assert.Null(payload.AwTr);
            Assert.Null(payload.Ma[0].MaTeTr);
            Assert.Null(payload.Ma[0].Se[0].SeNaTr);
        }

        // Player markets (GM-528): the pushed MaTeTr is COMPOSED from the cache's merged Pl: and
        // MaTe: state (the Worker Apply'd the delta before this path) — never the raw template
        // text, which would collapse the N per-player markets into one translated block.
        [Fact]
        public void ApplyTranslationDeltas_PlayerMarket_PlayerDelta_ComposesMaTeTrFromCache()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            payload.Ma[0].CompId = 150762;
            var cache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["Pl:150762"] = Delta("罗希特·夏尔马"),
                ["MaTe:MT1"] = Delta("第一局首位达到50分的球员 是/否")
            });

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: new(),
                translationCache: cache,
                playerDeltas: new Dictionary<int, List<TranslationMessage>> { [150762] = Delta("罗希特·夏尔马") });

            Assert.Equal("罗希特·夏尔马 - 第一局首位达到50分的球员 是/否", payload.Ma[0].MaTeTr!.Single().Tx);
        }

        [Fact]
        public void ApplyTranslationDeltas_PlayerMarket_TemplateDelta_ComposesInsteadOfRawText()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            payload.Ma[0].CompId = 150762;
            var templateDelta = Delta("第一局首位达到50分的球员 是/否");
            var cache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["Pl:150762"] = Delta("罗希特·夏尔马"),
                ["MaTe:MT1"] = templateDelta
            });

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>> { ["MT1"] = new() { (null, templateDelta) } },
                selectionTemplateDeltas: new(),
                translationCache: cache);

            Assert.Equal("罗希特·夏尔马 - 第一局首位达到50分的球员 是/否", payload.Ma[0].MaTeTr!.Single().Tx);
        }

        [Fact]
        public void ApplyTranslationDeltas_PlayerMarket_UntranslatedPlayer_PushesNothingForThatMarket()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            payload.Ma[0].CompId = 150762;
            var templateDelta = Delta("模板");
            var cache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["MaTe:MT1"] = templateDelta // no Pl:150762 — composing is impossible
            });

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>> { ["MT1"] = new() { (null, templateDelta) } },
                selectionTemplateDeltas: new(),
                translationCache: cache);

            Assert.Null(payload.Ma[0].MaTeTr);
        }

        // MaTeP: placeholder deltas (GM-528 interpolation): a translated placeholder that kept the
        // {player} marker yields the interpolated name; the join stays the fallback.
        [Fact]
        public void ApplyTranslationDeltas_PlayerTemplateDelta_InterpolatesFromCache()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            payload.Ma[0].CompId = 150762;
            var placeholderDelta = Delta("第一局{player}达到50分 是/否");
            var cache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["Pl:150762"] = Delta("费罗扎，海鸥"),
                ["MaTe:MT1"] = Delta("第一局首位达到50分的球员 是/否"),
                ["MaTeP:MT1"] = placeholderDelta
            });

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: new(),
                translationCache: cache,
                playerTemplateDeltas: new Dictionary<string, List<TranslationMessage>> { ["MT1"] = placeholderDelta });

            Assert.Equal("第一局费罗扎，海鸥达到50分 是/否", payload.Ma[0].MaTeTr!.Single().Tx);
        }

        [Fact]
        public void ApplyTranslationDeltas_PlayerMarket_UnrelatedDelta_StaysNull()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            payload.Ma[0].CompId = 150762;
            var cache = new StubTranslationCache(new Dictionary<string, List<TranslationMessage>>
            {
                ["Pl:150762"] = Delta("罗希特·夏尔马"),
                ["MaTe:MT1"] = Delta("模板")
            });

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: new(),
                translationCache: cache,
                playerDeltas: new Dictionary<int, List<TranslationMessage>> { [999999] = Delta("someone else") });

            Assert.Null(payload.Ma[0].MaTeTr);
        }

        #endregion

        #region RetryUntilMatched

        // Covers the 2026-07-09 fix for the update-only write race against OE.Sportsbook.Service's
        // own upsert (see MongoTranslationRepository.UpdateOneWithRetry / docs/TranslationFlowArchitecture.md
        // §2.1): these test the pure retry loop directly, since the class otherwise talks to a live
        // Mongo collection via a ctor-constructed MongoClient.

        [Fact]
        public void RetryUntilMatched_SucceedsFirstAttempt_ReturnsTrueWithoutDelaying()
        {
            var attempts = 0;
            var delays = 0;

            var result = MongoTranslationRepository.RetryUntilMatched(
                attempt: () => { attempts++; return true; },
                delay: _ => delays++,
                maxAttempts: 5,
                delayBetweenAttempts: TimeSpan.FromMilliseconds(200));

            Assert.True(result);
            Assert.Equal(1, attempts);
            Assert.Equal(0, delays);
        }

        [Fact]
        public void RetryUntilMatched_SucceedsOnThirdAttempt_StopsRetryingAfterTwoDelays()
        {
            var attempts = 0;
            var delays = 0;

            var result = MongoTranslationRepository.RetryUntilMatched(
                attempt: () => { attempts++; return attempts == 3; },
                delay: _ => delays++,
                maxAttempts: 5,
                delayBetweenAttempts: TimeSpan.FromMilliseconds(200));

            Assert.True(result);
            Assert.Equal(3, attempts);
            Assert.Equal(2, delays);
        }

        [Fact]
        public void RetryUntilMatched_NeverMatches_ExhaustsAllAttemptsAndReturnsFalse()
        {
            var attempts = 0;
            var delays = 0;

            var result = MongoTranslationRepository.RetryUntilMatched(
                attempt: () => { attempts++; return false; },
                delay: _ => delays++,
                maxAttempts: 5,
                delayBetweenAttempts: TimeSpan.FromMilliseconds(200));

            Assert.False(result);
            Assert.Equal(5, attempts);
            Assert.Equal(4, delays); // no delay is spent after the final, still-failing attempt
        }

        [Fact]
        public void RetryUntilMatched_PassesTheConfiguredDelaySpanToEachDelayCall()
        {
            var seenDelays = new List<TimeSpan>();
            var configuredDelay = TimeSpan.FromMilliseconds(200);

            MongoTranslationRepository.RetryUntilMatched(
                attempt: () => false,
                delay: seenDelays.Add,
                maxAttempts: 3,
                delayBetweenAttempts: configuredDelay);

            Assert.Equal(2, seenDelays.Count);
            Assert.All(seenDelays, d => Assert.Equal(configuredDelay, d));
        }

        #endregion
    }
}
