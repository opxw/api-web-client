// Copyright (c) 2026 - opx
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Opx.Api.Client;

public static class ServiceCollectionExtensions
{
	private const string DefaultWebSocketClientName = "default";

	public static IServiceCollection AddOpxApiWebSocketClient(
		this IServiceCollection services,
		string baseAddress,
		Action<OpxWebSocketClientOptions>? configure = null)
	{
		services.AddOpxApiWebSocketClient(DefaultWebSocketClientName, baseAddress, configure);
		services.TryAddScoped<IOpxWebSocketClient>(provider =>
			provider.GetRequiredService<IOpxWebSocketClientFactory>().CreateClient(DefaultWebSocketClientName));
		return services;
	}

	public static IServiceCollection AddOpxApiWebSocketClient(
		this IServiceCollection services,
		string name,
		string baseAddress,
		Action<OpxWebSocketClientOptions>? configure = null)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("WebSocket client name is required.", nameof(name));
		}

		var options = new OpxWebSocketClientOptions { BaseAddress = baseAddress };
		configure?.Invoke(options);
		services.AddSingleton(new OpxNamedWebSocketClientOptions(name, options));
		services.TryAddScoped<IOpxWebSocketClientFactory, OpxWebSocketClientFactory>();
		return services;
	}

	public static IHttpClientBuilder AddOpxApiClient(
		this IServiceCollection services,
		string baseAddress,
		Action<HttpClient>? configureHttpClient = null)
	{
		return services.AddOpxApiClient(options => options.BaseAddress = baseAddress, configureHttpClient);
	}

	public static IHttpClientBuilder AddOpxApiClient(
		this IServiceCollection services,
		string name,
		string baseAddress,
		Action<HttpClient>? configureHttpClient = null)
	{
		return services.AddOpxApiClient(name, options => options.BaseAddress = baseAddress, configureHttpClient);
	}

	public static IServiceCollection AddOpxInMemoryTokenProvider(
		this IServiceCollection services,
		ServiceLifetime lifetime = ServiceLifetime.Scoped,
		Func<IServiceProvider, OpxInMemoryTokenProvider>? factory = null)
	{
		var descriptor = factory is null
			? new ServiceDescriptor(typeof(IOpxTokenProvider), typeof(OpxInMemoryTokenProvider), lifetime)
			: new ServiceDescriptor(typeof(IOpxTokenProvider), provider => factory(provider), lifetime);

		services.Add(descriptor);
		return services;
	}

	public static IHttpClientBuilder AddOpxApiClient(
		this IServiceCollection services,
		Action<OpxApiClientOptions> configure,
		Action<HttpClient>? configureHttpClient = null)
	{
		services.TryAddScoped<IOpxApiClientFactory, OpxApiClientFactory>();
		var options = new OpxApiClientOptions();
		configure(options);

		services.AddSingleton(options);
		return services.AddHttpClient<IOpxApiClient, OpxApiClient>(client =>
		{
			if (!string.IsNullOrWhiteSpace(options.BaseAddress))
			{
				client.BaseAddress = new Uri(NormalizeBaseAddress(options.BaseAddress));
			}

			client.Timeout = options.Timeout;
			configureHttpClient?.Invoke(client);
		});
	}

	public static IHttpClientBuilder AddOpxApiClient(
		this IServiceCollection services,
		string name,
		Action<OpxApiClientOptions> configure,
		Action<HttpClient>? configureHttpClient = null)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("Client name is required.", nameof(name));
		}

		services.TryAddScoped<IOpxApiClientFactory, OpxApiClientFactory>();
		var options = new OpxApiClientOptions();
		configure(options);
		services.AddSingleton(new OpxNamedApiClientOptions(name, options));

		return services.AddHttpClient(name, client =>
		{
			if (!string.IsNullOrWhiteSpace(options.BaseAddress))
			{
				client.BaseAddress = new Uri(NormalizeBaseAddress(options.BaseAddress));
			}

			client.Timeout = options.Timeout;
			configureHttpClient?.Invoke(client);
		});
	}

	private static string NormalizeBaseAddress(string baseAddress)
	{
		return baseAddress.EndsWith("/", StringComparison.Ordinal)
			? baseAddress
			: string.Concat(baseAddress, "/");
	}
}
