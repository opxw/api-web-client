// Copyright (c) 2026 - opx
using System.Net.WebSockets;

namespace Opx.Api.Client;

public interface IOpxWebSocketClient : IAsyncDisposable
{
	WebSocketState State { get; }
	Task ConnectAsync(string path, CancellationToken cancellationToken = default);
	Task DisconnectAsync(CancellationToken cancellationToken = default);
	Task SubscribeAsync(string topic, CancellationToken cancellationToken = default);
	Task UnsubscribeAsync(string topic, CancellationToken cancellationToken = default);
	Task<string> SendAsync<T>(string type, T data, string? topic = null, bool requireAck = false, CancellationToken cancellationToken = default);
	IAsyncEnumerable<OpxWebSocketMessage> ReadAllAsync(CancellationToken cancellationToken = default);
	OpxWebSocketClientHealth GetHealth();
}

public interface IOpxWebSocketClientFactory
{
	IOpxWebSocketClient CreateClient(string name);
}
