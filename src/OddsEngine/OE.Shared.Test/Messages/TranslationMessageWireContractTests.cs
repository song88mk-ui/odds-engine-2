using System.Text.Json;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;

namespace OE.Shared.Test.Messages
{
    // The abbreviated property names ARE the wire contract (no serialization attributes) —
    // these tests pin the 2026-07 translation-flow additions so a rename can't silently break
    // bet-genius, the adapter enrichment, or the websocket delta path.
    public class TranslationMessageWireContractTests
    {
        [Fact]
        public void OnTranslationChangeMessage_WireCarriesExactNames()
        {
            var message = new OnTranslationChangeMessage
            {
                Tr = new List<TranslationChangeItem>
                {
                    new TranslationChangeItem { StId = "MaTe:16", LaCo = "ZH", Tx = "独赢" }
                }
            };

            var json = JsonSerializer.Serialize(message);

            using var document = JsonDocument.Parse(json);
            var item = document.RootElement.GetProperty("Tr")[0];

            Assert.Equal("MaTe:16", item.GetProperty("StId").GetString());
            Assert.Equal("ZH", item.GetProperty("LaCo").GetString());
            Assert.Equal("独赢", item.GetProperty("Tx").GetString());
        }

        [Fact]
        public void TranslationLists_RoundTripOnMarketChangeMessage()
        {
            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                SpTr = new List<TranslationMessage> { new() { LaCo = "ZH", Tx = "足球" } },
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        ChTyId = 0,
                        PrFiId = "1234567890",
                        PrMaId = "M1",
                        MaTeId = "16",
                        IsHa = true,
                        MaTeTr = new List<TranslationMessage> { new() { LaCo = "ZH", Tx = "独赢" } },
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                ChTyId = 0,
                                PrFiId = "1234567890",
                                PrMaId = "M1",
                                PrSeId = "S1",
                                SeNaTr = new List<TranslationMessage> { new() { LaCo = "ZH", Tx = "主队" } }
                            }
                        }
                    }
                }
            };

            var roundTripped = JsonSerializer.Deserialize<OnMarketChangeMessage>(JsonSerializer.Serialize(message));

            Assert.NotNull(roundTripped);
            Assert.Equal("足球", roundTripped!.SpTr!.Single().Tx);
            Assert.Equal("独赢", roundTripped.Ma.Single().MaTeTr!.Single().Tx);
            Assert.True(roundTripped.Ma.Single().IsHa);
            Assert.Equal("主队", roundTripped.Ma.Single().Se.Single().SeNaTr!.Single().Tx);
        }

        [Fact]
        public void TranslationLists_DefaultToNull_SoUnchangedDeltasStayOffTheWire()
        {
            var message = new OnFixtureChangeMessage { ChTyId = 0, PrFiId = "1234567890" };

            Assert.Null(message.SpTr);
            Assert.Null(message.SegTr);
            Assert.Null(message.LeTr);
            Assert.Null(message.HoTr);
            Assert.Null(message.AwTr);
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void OnTranslationChangeMessage_WireCarriesExactNames_MutationCheck()
        {
            var message = new OnTranslationChangeMessage
            {
                Tr = new List<TranslationChangeItem>
                {
                    new TranslationChangeItem { StId = "MaTe:16", LaCo = "ZH", Tx = "独赢" }
                }
            };

            var json = JsonSerializer.Serialize(message);

            using var document = JsonDocument.Parse(json);
            var item = document.RootElement.GetProperty("Tr")[0];

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("MaTe:999", item.GetProperty("StId").GetString()));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("EN", item.GetProperty("LaCo").GetString()));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong", item.GetProperty("Tx").GetString()));
        }

        [Fact]
        public void TranslationLists_RoundTripOnMarketChangeMessage_MutationCheck()
        {
            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                SpTr = new List<TranslationMessage> { new() { LaCo = "ZH", Tx = "足球" } },
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        ChTyId = 0,
                        PrFiId = "1234567890",
                        PrMaId = "M1",
                        MaTeId = "16",
                        IsHa = true,
                        MaTeTr = new List<TranslationMessage> { new() { LaCo = "ZH", Tx = "独赢" } },
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                ChTyId = 0,
                                PrFiId = "1234567890",
                                PrMaId = "M1",
                                PrSeId = "S1",
                                SeNaTr = new List<TranslationMessage> { new() { LaCo = "ZH", Tx = "主队" } }
                            }
                        }
                    }
                }
            };

            var roundTripped = JsonSerializer.Deserialize<OnMarketChangeMessage>(JsonSerializer.Serialize(message));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Null(roundTripped));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong", roundTripped!.SpTr!.Single().Tx));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong", roundTripped.Ma.Single().MaTeTr!.Single().Tx));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(roundTripped.Ma.Single().IsHa));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong", roundTripped.Ma.Single().Se.Single().SeNaTr!.Single().Tx));
        }

        [Fact]
        public void TranslationLists_DefaultToNull_SoUnchangedDeltasStayOffTheWire_MutationCheck()
        {
            var message = new OnFixtureChangeMessage { ChTyId = 0, PrFiId = "1234567890" };

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(message.SpTr));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(message.SegTr));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(message.LeTr));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(message.HoTr));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(message.AwTr));
        }

        #endregion
    }
}
