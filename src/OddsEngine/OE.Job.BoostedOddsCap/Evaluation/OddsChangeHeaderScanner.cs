using System.Text.Json;

namespace OE.Job.BoostedOddsCap.Evaluation
{
    // Reads the two top-level properties that decide whether an odds change message matters (the provider
    // fixture id and the fixture status) without deserialising it. The message can carry every market and
    // selection of a fixture, and almost every one is for a fixture nobody boosted, so those are dropped here
    // after reading a handful of tokens: the scan stops as soon as it has both values, which the producer
    // writes before the market list, and skips nested values it passes instead of materialising them.
    public static class OddsChangeHeaderScanner
    {
        private static ReadOnlySpan<byte> ProviderFixtureIdName => "PrFiId"u8;
        private static ReadOnlySpan<byte> FixtureStatusIdName => "FiStId"u8;

        // True when the provider fixture id was found. The status is -1 when the message did not carry one.
        public static bool TryRead(ReadOnlySpan<byte> json, out string providerFixtureId, out int fixtureStatusId)
        {
            providerFixtureId = null;
            fixtureStatusId = -1;

            var reader = new Utf8JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            var hasStatus = false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isFixtureId = reader.ValueTextEquals(ProviderFixtureIdName);
                var isStatus = !isFixtureId && reader.ValueTextEquals(FixtureStatusIdName);

                if (!reader.Read())
                    break;

                if (isFixtureId && reader.TokenType == JsonTokenType.String)
                    providerFixtureId = reader.GetString();
                else if (isStatus && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var status))
                {
                    fixtureStatusId = status;
                    hasStatus = true;
                }
                else
                    reader.Skip();

                if (providerFixtureId != null && hasStatus)
                    break;
            }

            return providerFixtureId != null;
        }
    }
}
