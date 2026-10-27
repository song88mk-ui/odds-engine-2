using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Sportsbook.Service.Repositories;
using System.Globalization;

namespace OE.Sportsbook.Service.Tests
{
    // The Backoffice price boost (OnPriceBoost) as OE.Sportsbook.Service applies it. The selection keeps one
    // "CODE:odds|CODE:odds" string, which the sportsbook site (Platform.Sportsbook.API
    // GetClientBoostedOdds) splits on '|' and ':', so its format is a contract with platform-final. These
    // cover the merge and the Mongo write models; the find and write against a real Mongo are not covered
    // here (this repo has no Mongo fixture) and are checked on DEV.
    public class PriceBoostTests
    {
        private static OnPriceBoostMessage Boost(string clientCode, decimal odds, bool isBoosted = true, string? name = null, string? translation = null)
        {
            var message = new OnPriceBoostMessage
            {
                ClientCode = clientCode,
                ProviderFixtureId = "14416699",
                MarketTemplateId = "10162",
                SelectionSpecifier = "bg:145241129:418700776",
                BoostedOdds = odds,
                IsBoosted = isBoosted
            };

            if (name != null)
                message.ClientBoostedSelectionNames.Add(new ClientBoostedSelectionNameMessage { ClientCode = clientCode, BoostedSelectionName = name });

            if (translation != null)
                message.ClientBoostedSelectionNameTranslations.Add(new ClientBoostedSelectionTranslationMessage { ClientCode = clientCode, LanguageCode = "ZH", BoostedSelectionName = translation });

            return message;
        }

        private static Selection Current(string? clBoOd, params string[] namedClients) => new()
        {
            Id = "0:1:1:1",
            ClBoOd = clBoOd!,
            ClBoSeNa = namedClients.Select(c => new ClientBoostedSelectionNameMDB { ClCo = c, BoSeNa = $"{c} name" }).ToList(),
            ClBoSeNaTr = namedClients.Select(c => new ClientBoostedSelectionNameTranslationMDB { ClCoLaCo = $"{c}:ZH", BoSeNa = $"{c} zh" }).ToList()
        };

        // ---- Merge ----

        [Fact]
        public void Merge_FirstBoostOfASelection_WritesTheClientEntryNameAndTranslation()
        {
            var result = PriceBoostMerger.Merge(Current(null), Boost("G02", 1.61m, name: "Asteras Tripolis", translation: "Asteras ZH"));

            Assert.Equal("G02:1.61", result.ClBoOd);
            var name = Assert.Single(result.ClBoSeNa);
            Assert.Equal("G02", name.ClCo);
            Assert.Equal("Asteras Tripolis", name.BoSeNa);
            var translation = Assert.Single(result.ClBoSeNaTr);
            Assert.Equal("G02:ZH", translation.ClCoLaCo);
            Assert.Equal("Asteras ZH", translation.BoSeNa);
        }

        [Fact]
        public void Merge_AnotherClient_KeepsTheBoostOfTheFirstAndSortsTheEntries()
        {
            var result = PriceBoostMerger.Merge(Current("G03:1.55", "G03"), Boost("G02", 1.61m, name: "Asteras Tripolis"));

            Assert.Equal("G02:1.61|G03:1.55", result.ClBoOd);
            Assert.Equal(new[] { "G03", "G02" }, result.ClBoSeNa.Select(n => n.ClCo));
            Assert.Equal("G03:ZH", Assert.Single(result.ClBoSeNaTr).ClCoLaCo);
        }

        [Fact]
        public void Merge_SameClientAgain_ReplacesItsEntryNameAndTranslationsInsteadOfAddingOnes()
        {
            var result = PriceBoostMerger.Merge(Current("G02:1.50|G03:1.55", "G02", "G03"), Boost("G02", 1.70m, name: "New name", translation: "New zh"));

            Assert.Equal("G02:1.70|G03:1.55", result.ClBoOd);
            Assert.Equal("G03 name", result.ClBoSeNa.Single(n => n.ClCo == "G03").BoSeNa);
            Assert.Equal("New name", result.ClBoSeNa.Single(n => n.ClCo == "G02").BoSeNa);
            Assert.Equal(2, result.ClBoSeNa.Count);
            Assert.Equal("New zh", result.ClBoSeNaTr.Single(t => t.ClCoLaCo == "G02:ZH").BoSeNa);
            Assert.Equal(2, result.ClBoSeNaTr.Count);
        }

        [Fact]
        public void Merge_BoostedOddsZero_RemovesOnlyThatClient()
        {
            var result = PriceBoostMerger.Merge(Current("G02:1.50|G03:1.55", "G02", "G03"), Boost("G02", 0m));

            Assert.Equal("G03:1.55", result.ClBoOd);
            Assert.Equal("G03", Assert.Single(result.ClBoSeNa).ClCo);
            Assert.Equal("G03:ZH", Assert.Single(result.ClBoSeNaTr).ClCoLaCo);
        }

        // Backoffice says IsBoosted false when no client boosts the fixture any more.
        [Fact]
        public void Merge_NotBoosted_RemovesTheClientEvenWithOddsOnTheMessage()
        {
            var result = PriceBoostMerger.Merge(Current("G02:1.50", "G02"), Boost("G02", 1.61m, isBoosted: false));

            Assert.Equal(string.Empty, result.ClBoOd);
            Assert.Empty(result.ClBoSeNa);
            Assert.Empty(result.ClBoSeNaTr);
        }

        [Fact]
        public void Merge_LastBoostRemoved_LeavesAnEmptyStringAndEmptyListsNotNulls()
        {
            var result = PriceBoostMerger.Merge(Current("G02:1.50"), Boost("G02", 0m));

            Assert.Equal(string.Empty, result.ClBoOd);
            Assert.NotNull(result.ClBoSeNa);
            Assert.NotNull(result.ClBoSeNaTr);
        }

        // The site reads the client by `Contains(clientCode)`, so the writer must never strip or replace
        // the entry of a client whose code merely starts or ends with the message client's.
        [Theory]
        [InlineData("G01:1.50", "G0", "G01:1.50|G0:1.61")]
        [InlineData("XG02:1.50", "G02", "G02:1.61|XG02:1.50")]
        [InlineData("G02:1.50|G022:1.80", "G02", "G022:1.80|G02:1.61")]
        public void Merge_MatchesTheClientByItsWholeCode(string current, string clientCode, string expected)
        {
            var result = PriceBoostMerger.Merge(Current(current), Boost(clientCode, 1.61m));

            Assert.Equal(expected, result.ClBoOd);
        }

        [Fact]
        public void Merge_RemovalByWholeCode_LeavesTheEntriesOfLongerCodes()
        {
            var result = PriceBoostMerger.Merge(Current("G02:1.50|G022:1.80", "G02", "G022"), Boost("G02", 0m));

            Assert.Equal("G022:1.80", result.ClBoOd);
            Assert.Equal("G022", Assert.Single(result.ClBoSeNa).ClCo);
            Assert.Equal("G022:ZH", Assert.Single(result.ClBoSeNaTr).ClCoLaCo);
        }

        [Fact]
        public void Merge_ClientCodeIsMatchedIgnoringCase()
        {
            var result = PriceBoostMerger.Merge(Current("g02:1.50", "g02"), Boost("G02", 1.61m, name: "Asteras Tripolis"));

            Assert.Equal("G02:1.61", result.ClBoOd);
            Assert.Equal("G02", Assert.Single(result.ClBoSeNa).ClCo);
        }

        [Fact]
        public void Merge_EntriesAreSortedAsOrdinalStrings_SoTheStoredValueDoesNotDependOnTheServerCulture()
        {
            var result = PriceBoostMerger.Merge(Current("G10:1.5|G1:1.5|g2:1.5"), Boost("G02", 1.61m));

            // '0' sorts before ':', so "G10:" comes before "G1:"; lower case after upper case.
            Assert.Equal("G02:1.61|G10:1.5|G1:1.5|g2:1.5", result.ClBoOd);
        }

        // The site would throw on an entry without ':' (Split(":")[1]) for the client it names.
        [Fact]
        public void Merge_DropsEntriesThatAreNotCodeColonOdds()
        {
            var result = PriceBoostMerger.Merge(Current("G03|G04:1.40||  "), Boost("G02", 1.61m));

            Assert.Equal("G02:1.61|G04:1.40", result.ClBoOd);
        }

        // The sportsbook server parses the odds with its own culture: the writer must not depend on the one it runs in.
        [Fact]
        public void Merge_FormatsTheOddsWithTheInvariantCulture()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                var result = PriceBoostMerger.Merge(Current(null), Boost("G02", 1.61m));

                Assert.Equal("G02:1.61", result.ClBoOd);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        // One message is one client's boost: names or translations the message carries for another client are not written.
        [Fact]
        public void Merge_IgnoresNamesAndTranslationsOfOtherClientsOnTheMessage()
        {
            var message = Boost("G02", 1.61m, name: "Asteras Tripolis");
            message.ClientBoostedSelectionNames.Add(new ClientBoostedSelectionNameMessage { ClientCode = "G03", BoostedSelectionName = "Other client" });
            message.ClientBoostedSelectionNameTranslations.Add(new ClientBoostedSelectionTranslationMessage { ClientCode = "G03", LanguageCode = "ZH", BoostedSelectionName = "Other zh" });

            var result = PriceBoostMerger.Merge(Current(null), message);

            Assert.Equal("G02", Assert.Single(result.ClBoSeNa).ClCo);
            Assert.Empty(result.ClBoSeNaTr);
        }

        [Fact]
        public void Merge_SelectionWithoutBoostFieldsAtAll_IsTreatedAsNotBoosted()
        {
            var selection = new Selection { Id = "0:1:1:1" };

            var result = PriceBoostMerger.Merge(selection, Boost("G02", 1.61m));

            Assert.Equal("G02:1.61", result.ClBoOd);
        }

        // ---- Flags ----

        [Fact]
        public void Flags_NoBoostedSelection_IsNotBoostedWithNoClients()
        {
            var flags = PriceBoostMerger.Flags(new string?[] { null, string.Empty, "  " }!);

            Assert.False(flags.IsBo);
            Assert.Equal(string.Empty, flags.ClBo);
        }

        [Fact]
        public void Flags_ClientsOfAllTheSelections_AreJoinedDistinctAndSorted()
        {
            var flags = PriceBoostMerger.Flags(new[] { "G03:1.6|G02:1.5", "G02:2.0", string.Empty });

            Assert.True(flags.IsBo);
            Assert.Equal("G02|G03", flags.ClBo);
        }

        [Fact]
        public void Flags_AMarketWhoseOnlyBoostWasRemoved_IsNoLongerBoosted()
        {
            var removed = PriceBoostMerger.Merge(Current("G02:1.50"), Boost("G02", 0m));

            var flags = PriceBoostMerger.Flags(new[] { removed.ClBoOd });

            Assert.False(flags.IsBo);
            Assert.Equal(string.Empty, flags.ClBo);
        }

        // ---- Mongo write models ----

        private static BsonDocument Render<T>(UpdateDefinition<T> update) =>
            update.Render(new RenderArgs<T>(BsonSerializer.SerializerRegistry.GetSerializer<T>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

        private static BsonDocument Render<T>(FilterDefinition<T> filter) =>
            filter.Render(new RenderArgs<T>(BsonSerializer.SerializerRegistry.GetSerializer<T>(), BsonSerializer.SerializerRegistry));

        [Fact]
        public void ToSelectionBoosted_SetsTheThreeBoostFieldsOnTheSelectionById_AndNeverCreatesOne()
        {
            var boost = PriceBoostMerger.Merge(Current("G03:1.55", "G03"), Boost("G02", 1.61m, name: "Asteras Tripolis", translation: "Asteras ZH"));

            var write = Assert.IsType<UpdateOneModel<Selection>>(Assert.Single(SportsbookRepository.ToSelectionBoosted("0:1:1:1", boost)));

            Assert.False(write.IsUpsert);
            Assert.Equal("0:1:1:1", Render(write.Filter)["_id"].AsString);
            var set = Render(write.Update)["$set"].AsBsonDocument;
            Assert.Equal(new[] { "ClBoOd", "ClBoSeNa", "ClBoSeNaTr" }, set.Names.OrderBy(n => n));
            Assert.Equal("G02:1.61|G03:1.55", set["ClBoOd"].AsString);
            Assert.Equal(2, set["ClBoSeNa"].AsBsonArray.Count);
            Assert.Equal(2, set["ClBoSeNaTr"].AsBsonArray.Count);
        }

        [Fact]
        public void ToSelectionBoosted_RemovedBoost_WritesAnEmptyStringAndEmptyArrays()
        {
            var boost = PriceBoostMerger.Merge(Current("G02:1.50", "G02"), Boost("G02", 0m));

            var write = Assert.IsType<UpdateOneModel<Selection>>(Assert.Single(SportsbookRepository.ToSelectionBoosted("0:1:1:1", boost)));

            var set = Render(write.Update)["$set"].AsBsonDocument;
            Assert.Equal(string.Empty, set["ClBoOd"].AsString);
            Assert.Empty(set["ClBoSeNa"].AsBsonArray);
            Assert.Empty(set["ClBoSeNaTr"].AsBsonArray);
        }

        [Fact]
        public void ToMarketBoosted_SetsOnlyTheFlagsOfTheMarketByMaId()
        {
            var write = Assert.IsType<UpdateOneModel<Market>>(Assert.Single(SportsbookRepository.ToMarketBoosted(42, (true, "G02|G03"))));

            Assert.False(write.IsUpsert);
            Assert.Equal(42L, Render(write.Filter)["MaId"].AsInt64);
            var set = Render(write.Update)["$set"].AsBsonDocument;
            Assert.Equal(new[] { "ClBo", "IsBo" }, set.Names.OrderBy(n => n));
            Assert.True(set["IsBo"].AsBoolean);
            Assert.Equal("G02|G03", set["ClBo"].AsString);
        }

        [Fact]
        public void ToFixtureBoosted_SetsOnlyTheFlagsOfTheFixtureById()
        {
            var write = Assert.IsType<UpdateOneModel<Fixture>>(Assert.Single(SportsbookRepository.ToFixtureBoosted("0:14416699", (false, string.Empty))));

            Assert.False(write.IsUpsert);
            Assert.Equal("0:14416699", Render(write.Filter)["_id"].AsString);
            var set = Render(write.Update)["$set"].AsBsonDocument;
            Assert.Equal(new[] { "ClBo", "IsBo" }, set.Names.OrderBy(n => n));
            Assert.False(set["IsBo"].AsBoolean);
            Assert.Equal(string.Empty, set["ClBo"].AsString);
        }
    }
}
