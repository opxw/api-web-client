// Copyright (c) 2026 - opx
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opx.Api.Web;
using Opx.Api.Web.WebSockets;

namespace Opx.Api.Client.Tests;

[TestFixture]
public sealed class OpxWebSocketClientTests
{
	[Test]
	public async Task Client_SubscribeAckAndTopicDelivery_WorkEndToEnd()
	{
		await using var server = await CreateServerAsync();
		await using var services = CreateClientServices(server.Urls.Single());
		await using var client = services.GetRequiredService<IOpxWebSocketClientFactory>().CreateClient("realtime");
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		await client.ConnectAsync("/ws", timeout.Token);
		await client.SubscribeAsync("sales", timeout.Token);
		await server.Services.GetRequiredService<IOpxWebSocketConnectionManager>()
			.SendToTopicAsync("sales", "sales.updated", new { total = 77 }, timeout.Token);
		var message = await ReadUntilAsync(client, "sales.updated", timeout.Token);

		Assert.Multiple(() =>
		{
			Assert.That(message.GetData<JsonElement>().GetProperty("total").GetInt32(), Is.EqualTo(77));
			Assert.That(client.GetHealth().Subscriptions, Is.EqualTo(1));
			Assert.That(client.State, Is.EqualTo(WebSocketState.Open));
		});
	}

	[Test]
	public async Task Client_AfterDisconnect_ReconnectsAndResubscribes()
	{
		await using var server = await CreateServerAsync();
		await using var services = CreateClientServices(server.Urls.Single());
		await using var client = services.GetRequiredService<IOpxWebSocketClientFactory>().CreateClient("realtime");
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

		await client.ConnectAsync("/ws", timeout.Token);
		await client.SubscribeAsync("inventory", timeout.Token);
		var probe = server.Services.GetRequiredService<ConnectionProbe>();
		var firstSession = await probe.ReadAsync(timeout.Token);
		firstSession.Abort();
		await WaitUntilAsync(() => client.GetHealth().Reconnects >= 1, timeout.Token);

		await server.Services.GetRequiredService<IOpxWebSocketConnectionManager>()
			.SendToTopicAsync("inventory", "inventory.updated", new { stock = 9 }, timeout.Token);
		var message = await ReadUntilAsync(client, "inventory.updated", timeout.Token);

		Assert.That(message.GetData<JsonElement>().GetProperty("stock").GetInt32(), Is.EqualTo(9));
	}

	[Test]
	public async Task Client_Heartbeat_UpdatesPongHealth()
	{
		await using var server = await CreateServerAsync();
		await using var services = CreateClientServices(server.Urls.Single(), options =>
		{
			options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
			options.HeartbeatTimeout = TimeSpan.FromSeconds(2);
		});
		await using var client = services.GetRequiredService<IOpxWebSocketClientFactory>().CreateClient("realtime");
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		await client.ConnectAsync("/ws", timeout.Token);
		var connectedAt = client.GetHealth().ConnectedAt;
		await WaitUntilAsync(() => client.GetHealth().LastPongAt > connectedAt, timeout.Token);

		Assert.That(client.GetHealth().Connected, Is.True);
	}

	[Test]
	public async Task Client_FiveHundredAcknowledgedMessages_CompletesWithinFiveSeconds()
	{
		await using var server = await CreateServerAsync();
		await using var services = CreateClientServices(server.Urls.Single(), options => options.HeartbeatInterval = TimeSpan.Zero);
		await using var client = services.GetRequiredService<IOpxWebSocketClientFactory>().CreateClient("realtime");
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		await client.ConnectAsync("/ws", timeout.Token);
		var stopwatch = Stopwatch.StartNew();

		for (var index = 0; index < 500; index++)
		{
			await client.SendAsync("opx.ping", new { index }, requireAck: true, cancellationToken: timeout.Token);
		}

		stopwatch.Stop();
		TestContext.Out.WriteLine($"Opx WebSocket client 500 acknowledged messages: {stopwatch.ElapsedMilliseconds} ms");
		Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
	}

	private static async Task<WebApplication> CreateServerAsync()
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["OpxApiProtection:WebSocket:Enabled"] = "true",
			["OpxApiProtection:WebSocket:Path"] = "/ws",
			["OpxApiProtection:WebSocket:RequireAuthorization"] = "false",
			["OpxApiProtection:WebSocket:MessageRateLimit"] = "5000",
			["OpxApiProtection:SecurityIssueLog:Enabled"] = "false"
		});
		builder.Services.UseOpxWebApi();
		builder.Services.AddSingleton<ConnectionProbe>();
		builder.Services.AddOpxWebSocket<ProbeWebSocketHandler>();
		var app = builder.Build();
		app.UseOpxWebApiHandler();
		await app.StartAsync();
		return app;
	}

	private static ServiceProvider CreateClientServices(string baseAddress, Action<OpxWebSocketClientOptions>? configure = null)
	{
		var services = new ServiceCollection();
		services.AddOpxApiWebSocketClient("realtime", baseAddress, options =>
		{
			options.ReconnectInitialDelay = TimeSpan.FromMilliseconds(20);
			options.ReconnectMaxDelay = TimeSpan.FromMilliseconds(100);
			options.AckTimeout = TimeSpan.FromSeconds(2);
			options.HeartbeatInterval = TimeSpan.FromSeconds(30);
			configure?.Invoke(options);
		});
		return services.BuildServiceProvider();
	}

	private static async Task<OpxWebSocketMessage> ReadUntilAsync(IOpxWebSocketClient client, string type, CancellationToken cancellationToken)
	{
		await foreach (var message in client.ReadAllAsync(cancellationToken))
		{
			if (message.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
			{
				return message;
			}
		}

		throw new InvalidOperationException($"WebSocket message '{type}' was not received.");
	}

	private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
	{
		while (!condition())
		{
			cancellationToken.ThrowIfCancellationRequested();
			await Task.Delay(20, cancellationToken);
		}
	}

	public sealed class ConnectionProbe
	{
		private readonly ConcurrentQueue<OpxWebSocketSession> _sessions = new();
		private readonly SemaphoreSlim _signal = new(0);
		public void Add(OpxWebSocketSession session)
		{
			_sessions.Enqueue(session);
			_signal.Release();
		}
		public async Task<OpxWebSocketSession> ReadAsync(CancellationToken cancellationToken)
		{
			await _signal.WaitAsync(cancellationToken);
			_sessions.TryDequeue(out var session);
			return session!;
		}
	}

	public sealed class ProbeWebSocketHandler(ConnectionProbe probe) : OpxWebSocketHandler
	{
		public override ValueTask OnConnectedAsync(OpxWebSocketSession session, CancellationToken cancellationToken)
		{
			probe.Add(session);
			return ValueTask.CompletedTask;
		}
	}
}
