using System.Net;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace OE.Websocket.Service.Tests
{
    // SportsbookRepository publishes through HubConnection.SendAsync, which forwards to the
    // virtual SendCoreAsync. Overriding that captures the exact payload the platform would
    // receive without a hub, a transport, or a network - the only way to assert the SignalR
    // wire contract (CLAUDE.md non-negotiable fact 4) in a unit test.
    //
    // The base constructor never opens a connection: it only stores the factory, so the
    // ThrowingConnectionFactory below is never invoked as long as no test calls StartAsync.
    public sealed class RecordingHubConnection : HubConnection
    {
        public RecordingHubConnection()
            : base(
                new ThrowingConnectionFactory(),
                new JsonHubProtocol(),
                new DnsEndPoint("recording.invalid", 80),
                new ServiceCollection().BuildServiceProvider(),
                NullLoggerFactory.Instance)
        {
        }

        public List<SentMessage> Sent { get; } = new();

        public override Task SendCoreAsync(string methodName, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add(new SentMessage(methodName, args));

            return Task.CompletedTask;
        }

        public T Payload<T>(int index = 0) => Assert.IsType<T>(Sent[index].Args[1]);

        public string ClientCode(int index = 0) => Assert.IsType<string>(Sent[index].Args[0]);

        public sealed record SentMessage(string Method, object?[] Args);

        private sealed class ThrowingConnectionFactory : IConnectionFactory
        {
            public ValueTask<ConnectionContext> ConnectAsync(EndPoint endpoint, CancellationToken cancellationToken = default)
                => throw new NotSupportedException("RecordingHubConnection never connects.");
        }
    }
}
