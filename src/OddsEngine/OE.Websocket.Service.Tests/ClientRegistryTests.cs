using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OE.WebSocket.Service.Clients;

namespace OE.Websocket.Service.Tests
{
    // Covers the GM-720 multi-tenant client registry: Platform API discovery
    // (stubbed) and the routing-key / ClientId resolution that keeps user-specific
    // messages on the correct client.
    public class ClientRegistryTests
    {
        // Stubs the Platform API v1/clients call; everything else is real.
        private class StubbedClientRegistry : ClientRegistry
        {
            public List<ClientDiscoveryDTO> Discovered { get; set; }

            public StubbedClientRegistry(IConfiguration configuration, List<ClientDiscoveryDTO> discovered)
                : base(configuration, NullLogger<ClientRegistry>.Instance)
            {
                Discovered = discovered;
            }

            protected override Task<List<ClientDiscoveryDTO>> DiscoverClients() => Task.FromResult(Discovered);
        }

        private static IConfiguration BuildConfiguration(Dictionary<string, string?> settings)
        {
            settings.TryAdd("Custom:PlatformAPI", "http://localhost:3000");

            return new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
        }

        private static StubbedClientRegistry BuildRegistry(
            Dictionary<string, string?> settings = null,
            List<ClientDiscoveryDTO> discovered = null)
        {
            return new StubbedClientRegistry(BuildConfiguration(settings ?? new Dictionary<string, string?>()), discovered);
        }

        private static List<ClientDiscoveryDTO> TwoActiveClients() => new()
        {
            new ClientDiscoveryDTO { ClientId = "F5251362-64F1-4BF4-28E9-08D8EE99CA8E", ClientCode = "S88", IsActive = true },
            new ClientDiscoveryDTO { ClientId = "087DFD95-7412-4AD4-924D-9498C4434790", ClientCode = "GSA", IsActive = true }
        };

        [Fact]
        public async Task Discovery_TakesActiveClients_LowercasesCodes()
        {
            var discovered = TwoActiveClients();
            discovered.Add(new ClientDiscoveryDTO { ClientId = "11111111-1111-1111-1111-111111111111", ClientCode = "OLD", IsActive = false });

            var registry = BuildRegistry(discovered: discovered);

            var added = await registry.RefreshAsync();

            Assert.Equal(2, added.Count);
            Assert.Contains(registry.Clients, c => c.ClientCode == "s88");
            Assert.Contains(registry.Clients, c => c.ClientCode == "gsa");
            Assert.DoesNotContain(registry.Clients, c => c.ClientCode == "old");
        }

        [Fact]
        public async Task DiscoveryFailure_KeepsCurrentClientList()
        {
            var registry = BuildRegistry(discovered: TwoActiveClients());

            await registry.RefreshAsync();
            Assert.Equal(2, registry.Clients.Count);

            registry.Discovered = null;
            var added = await registry.RefreshAsync();

            Assert.Empty(added);
            Assert.Equal(2, registry.Clients.Count);
        }

        [Fact]
        public async Task ResolveByRoutingKey_MatchesClientSuffix_OnlyForKnownCodes()
        {
            var registry = BuildRegistry(discovered: TwoActiveClients());

            await registry.RefreshAsync();

            Assert.Equal("gsa", registry.ResolveByRoutingKey("r.bet.betstatus.gsa")?.ClientCode);
            Assert.Equal("s88", registry.ResolveByRoutingKey("r.oe.sb.engine.onvisitfavorites.s88")?.ClientCode);
            Assert.Null(registry.ResolveByRoutingKey("r.oe.sb.engine.onoddschange"));
            Assert.Null(registry.ResolveByRoutingKey("r.bet.betstatus.qte"));
            Assert.Null(registry.ResolveByRoutingKey(null!));
        }

        [Fact]
        public async Task ResolveByClientId_MatchesCaseInsensitive()
        {
            var registry = BuildRegistry(discovered: TwoActiveClients());

            await registry.RefreshAsync();

            Assert.Equal("gsa", registry.ResolveByClientId("087dfd95-7412-4ad4-924d-9498c4434790")?.ClientCode);
            Assert.Null(registry.ResolveByClientId("00000000-0000-0000-0000-000000000000"));
            Assert.Null(registry.ResolveByClientId(null!));
        }

        [Fact]
        public async Task Refresh_KeepsExistingContexts_ReportsNoAdds()
        {
            var registry = BuildRegistry(discovered: TwoActiveClients());

            await registry.RefreshAsync();
            var original = registry.Clients.Single(c => c.ClientCode == "s88");

            var added = await registry.RefreshAsync();

            Assert.Empty(added);
            Assert.Same(original, registry.Clients.Single(c => c.ClientCode == "s88"));
        }
    }
}
