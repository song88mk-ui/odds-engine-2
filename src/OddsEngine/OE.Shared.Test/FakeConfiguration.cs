using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace OE.Shared.Test
{
    // Minimal IConfiguration stand-in covering only the flat-key indexer lookups this codebase's
    // imperative config style relies on (cfg["A:B"]) — no ConfigurationBuilder/provider packages
    // needed. GetSection/GetChildren/GetReloadToken aren't used by anything under test here.
    public class FakeConfiguration : IConfiguration
    {
        private readonly Dictionary<string, string?> _values;

        public FakeConfiguration(Dictionary<string, string?> values)
        {
            _values = values;
        }

        public string? this[string key]
        {
            get => _values.TryGetValue(key, out var value) ? value : null;
            set => _values[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() => throw new NotImplementedException();

        public IChangeToken GetReloadToken() => throw new NotImplementedException();

        public IConfigurationSection GetSection(string key) => throw new NotImplementedException();
    }
}
