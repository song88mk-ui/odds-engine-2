namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    // One translated value per language, carried on the *Tr properties
    // (SpTr/SegTr/LeTr/HoTr/AwTr on OnFixtureChangeMessage, MaTeTr on MarketMessage,
    // SeNaTr on SelectionMessage). A null or empty list means "no translation available"
    // or "unchanged since last publish" — consumers must fall back to the English *Na source.
    public class TranslationMessage
    {
        public string LaCo { get; set; } // target language code, e.g. "ZH" | "ES" | "FR"
        public string Tx { get; set; }   // translated text
    }
}
