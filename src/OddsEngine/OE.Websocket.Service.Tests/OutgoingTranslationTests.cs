using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.WebSocket.Service.Repository;

namespace OE.Websocket.Service.Tests
{
    // Covers the websocket-bound count>0 guard (2026-07 translation-flow redesign): a *Tr list is
    // forwarded to Platform.Websocket.API2 only when it carries entries; null keeps the property
    // off the wire so clients retain their current translated names.
    public class OutgoingTranslationTests
    {
        [Fact]
        public void NullInput_StaysOffTheWire()
        {
            Assert.Null(SportsbookRepository.ToOutgoingTranslations(null!));
        }

        [Fact]
        public void EmptyInput_StaysOffTheWire()
        {
            Assert.Null(SportsbookRepository.ToOutgoingTranslations(new List<TranslationMessage>()));
        }

        [Fact]
        public void Entries_MapLanguageAndTextVerbatim()
        {
            var outgoing = SportsbookRepository.ToOutgoingTranslations(new List<TranslationMessage>
            {
                new TranslationMessage { LaCo = "ZH", Tx = "足球" },
                new TranslationMessage { LaCo = "FR", Tx = "Football" }
            });

            Assert.NotNull(outgoing);
            Assert.Equal(2, outgoing!.Count);
            Assert.Equal("足球", outgoing.Single(t => t.LaCo == "ZH").Tx);
            Assert.Equal("Football", outgoing.Single(t => t.LaCo == "FR").Tx);
        }
    }
}
