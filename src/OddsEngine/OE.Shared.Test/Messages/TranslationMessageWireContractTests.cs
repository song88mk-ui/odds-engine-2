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
        public void OnTranslationChangeMessage_WireCarriesSrcTx()
        {
            // GM-852: SrcTx (added in GM-646, MaTe wording-variant scoping + content-addressed
            // index) was declared on this class from day one, but no sender ever populated it,
            // so it was always null in production. This pins that the wire name is exactly SrcTx
            // once a sender does set it.
            var message = new OnTranslationChangeMessage
            {
                Tr = new List<TranslationChangeItem>
                {
                    new TranslationChangeItem { StId = "MaTe:16", LaCo = "ZH", Tx = "独赢", SrcTx = "Match Winner" }
                }
            };

            var json = JsonSerializer.Serialize(message);

            using var document = JsonDocument.Parse(json);
            var item = document.RootElement.GetProperty("Tr")[0];

            Assert.Equal("Match Winner", item.GetProperty("SrcTx").GetString());
        }

        [Fact]
        public void OnTranslationChangeMessage_BindsSrcTx_FromEitherPublisherShape()
        {
            // GM-852 regression guard: platform-final's Platform.Shared.MessageModels and
            // backoffice-final's Backoffice.Shared.OutgoingEngineMessages each keep their own
            // hand-maintained copy of TranslationChangeItem (no shared package across the three
            // repos), and it was exactly this drift, one copy missing a field, that left SrcTx
            // permanently null. Both copies use the SAME wire names as this class (StId, LaCo,
            // Tx, SrcTx), so a payload shaped like either publisher's JSON output must still bind
            // correctly here — this literal represents that shape without a cross-repo reference.
            const string payloadFromEitherPublisher = @"{
                ""Tr"": [
                    { ""StId"": ""MaTe:259~a1b2c3d4"", ""LaCo"": ""ZH"", ""Tx"": ""大/小"", ""SrcTx"": ""Over/Under"" }
                ]
            }";

            var message = JsonSerializer.Deserialize<OnTranslationChangeMessage>(payloadFromEitherPublisher);

            Assert.NotNull(message);
            var item = message!.Tr.Single();
            Assert.Equal("MaTe:259~a1b2c3d4", item.StId);
            Assert.Equal("ZH", item.LaCo);
            Assert.Equal("大/小", item.Tx);
            Assert.Equal("Over/Under", item.SrcTx);
        }

        [Fact]
        public void OnTranslationChangeMessage_SrcTx_StaysNull_WhenThePublisherOmitsIt()
        {
            // A sender that has not been updated yet (or an older message on the wire during a
            // rolling deploy) omits sourceText/SrcTx entirely. Consumers must keep tolerating
            // that, per TranslationChangeItem's own contract comment.
            const string payloadFromAnUnupdatedSender = @"{
                ""Tr"": [ { ""StId"": ""Te:H1"", ""LaCo"": ""ZH"", ""Tx"": ""阿森纳"" } ]
            }";

            var message = JsonSerializer.Deserialize<OnTranslationChangeMessage>(payloadFromAnUnupdatedSender);

            Assert.NotNull(message);
            Assert.Null(message!.Tr.Single().SrcTx);
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
    }
}
