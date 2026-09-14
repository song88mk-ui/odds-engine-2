using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Utilities;
using OE.WebSocket.Service.Clients;
using OE.WebSocket.Service.Repository;
using OE.WebSocket.Service.Telemetry;

namespace OE.Websocket.Service.Tests
{
    // The odds maths and the SignalR group builders are private members of
    // SportsbookRepository, reached here by reflection. The alternative - widening them to
    // internal plus InternalsVisibleTo - would change production code to suit the tests, and
    // CLAUDE.md asks for the smallest possible change. If a member is renamed these helpers
    // fail on the lookup rather than silently passing.
    internal static class SportsbookRepositoryInternals
    {
        internal static IConfiguration BuildConfiguration(bool isOEEnabled = false) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Custom:IsOEEnabled", isOEEnabled.ToString() }
                })
                .Build();

        internal static SportsbookRepository BuildRepository(IMemCache memCache, bool isOEEnabled = false) =>
            new(BuildConfiguration(isOEEnabled), memCache, new Instrumentation(), NullLogger<SportsbookRepository>.Instance);

        internal static ClientContext BuildClient(string clientCode) =>
            new()
            {
                ClientId = "11111111-1111-1111-1111-111111111111",
                ClientCode = clientCode,
                HttpClient = new HttpClient { BaseAddress = new Uri("http://platform-api.test/") }
            };

        // Guid-suffixed so tests never collide in the process-wide MemoryCache.Default that
        // MemCache wraps.
        internal static string NewClientCode() => $"t{Guid.NewGuid():N}"[..12];

        internal static TResult InvokeStatic<TResult>(string name, params object[] arguments)
        {
            var method = Find(name, BindingFlags.Static | BindingFlags.NonPublic, arguments);

            return (TResult)method.Invoke(null, arguments)!;
        }

        internal static TResult Invoke<TResult>(SportsbookRepository repository, string name, params object[] arguments)
        {
            var method = Find(name, BindingFlags.Instance | BindingFlags.NonPublic, arguments);

            return (TResult)method.Invoke(repository, arguments)!;
        }

        private static MethodInfo Find(string name, BindingFlags flags, object[] arguments)
        {
            var candidates = typeof(SportsbookRepository)
                .GetMethods(flags)
                .Where(m => m.Name == name && m.GetParameters().Length == arguments.Length)
                .ToList();

            var method = candidates.Count == 1
                ? candidates[0]
                : candidates.SingleOrDefault(m => Matches(m, arguments));

            Assert.NotNull(method);

            return method!;
        }

        private static bool Matches(MethodInfo method, object[] arguments) =>
            method.GetParameters()
                .Select((p, i) => arguments[i] == null || p.ParameterType.IsInstanceOfType(arguments[i]))
                .All(match => match);
    }
}
