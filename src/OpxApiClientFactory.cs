// Copyright (c) 2026 - opx
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Opx.Api.Client;

internal sealed class OpxApiClientFactory : IOpxApiClientFactory
{
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly Dictionary<string, OpxApiClientOptions> _clients;
	private readonly IServiceProvider _serviceProvider;

	public OpxApiClientFactory(
		IHttpClientFactory httpClientFactory,
		IEnumerable<OpxNamedApiClientOptions> clients,
		IServiceProvider serviceProvider)
	{
		_httpClientFactory = httpClientFactory;
		_clients = clients.ToDictionary(client => client.Name, client => client.Options, StringComparer.OrdinalIgnoreCase);
		_serviceProvider = serviceProvider;
	}

	public IOpxApiClient CreateClient(string name)
	{
		if (!_clients.TryGetValue(name, out var options))
		{
			throw new InvalidOperationException($"Opx API client '{name}' is not registered.");
		}

		var httpClient = _httpClientFactory.CreateClient(name);
		var tokenProvider = _serviceProvider.GetService<IOpxTokenProvider>();
		var logger = _serviceProvider.GetService<ILogger<OpxApiClient>>();
		return new OpxApiClient(httpClient, options, tokenProvider, logger);
	}
}
