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

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void NullInput_StaysOffTheWire_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(SportsbookRepository.ToOutgoingTranslations(null!)));
        }

        [Fact]
        public void EmptyInput_StaysOffTheWire_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(SportsbookRepository.ToOutgoingTranslations(new List<TranslationMessage>())));
        }

        [Fact]
        public void Entries_MapLanguageAndTextVerbatim_MutationCheck()
        {
            var outgoing = SportsbookRepository.ToOutgoingTranslations(new List<TranslationMessage>
            {
                new TranslationMessage { LaCo = "ZH", Tx = "足球" },
                new TranslationMessage { LaCo = "FR", Tx = "Football" }
            });

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Null(outgoing));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(3, outgoing!.Count));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("Football", outgoing.Single(t => t.LaCo == "ZH").Tx));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("足球", outgoing.Single(t => t.LaCo == "FR").Tx));
        }

        #endregion
    }
}
