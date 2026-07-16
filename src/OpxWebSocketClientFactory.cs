// Copyright (c) 2026 - opx
using Microsoft.Extensions.DependencyInjection;

namespace Opx.Api.Client;

internal sealed class OpxWebSocketClientFactory : IOpxWebSocketClientFactory
{
	private readonly IReadOnlyDictionary<string, OpxWebSocketClientOptions> _clients;
	private readonly IServiceProvider _services;

	public OpxWebSocketClientFactory(IEnumerable<OpxNamedWebSocketClientOptions> clients, IServiceProvider services)
	{
		_clients = clients.ToDictionary(client => client.Name, client => client.Options, StringComparer.OrdinalIgnoreCase);
		_services = services;
	}

	public IOpxWebSocketClient CreateClient(string name)
	{
		if (!_clients.TryGetValue(name, out var options))
		{
			throw new InvalidOperationException($"Opx WebSocket client '{name}' is not registered.");
		}

		return new OpxWebSocketClient(options, _services.GetService<IOpxTokenProvider>());
	}
}
