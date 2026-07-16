// Copyright (c) 2026 - opx
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace Opx.Api.Client;

internal sealed class OpxWebSocketClient : IOpxWebSocketClient
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly SemaphoreSlim _connectLock = new(1, 1);
	private readonly Channel<OpxWebSocketMessage> _incoming;
	private readonly OpxWebSocketClientOptions _options;
	private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingAcks = new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly ConcurrentDictionary<string, byte> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly IOpxTokenProvider? _tokenProvider;
	private CancellationTokenSource? _lifetime;
	private Task? _heartbeatTask;
	private Task? _receiveTask;
	private ClientWebSocket? _socket;
	private string? _path;
	private DateTimeOffset? _connectedAt;
	private long _lastPongUnixMilliseconds;
	private long _lastReceivedUnixMilliseconds;
	private long _lastSentUnixMilliseconds;
	private long _messagesReceived;
	private long _messagesSent;
	private long _reconnects;
	private int _disconnecting;
	private int _disposed;

	public OpxWebSocketClient(OpxWebSocketClientOptions options, IOpxTokenProvider? tokenProvider)
	{
		_options = options;
		_tokenProvider = tokenProvider;
		_incoming = Channel.CreateBounded<OpxWebSocketMessage>(new BoundedChannelOptions(Math.Max(1, options.IncomingMessageCapacity))
		{
			SingleReader = false,
			SingleWriter = true,
			FullMode = BoundedChannelFullMode.Wait
		});
	}

	public WebSocketState State => Volatile.Read(ref _socket)?.State ?? WebSocketState.None;

	public async Task ConnectAsync(string path, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("WebSocket path is required.", nameof(path));
		}

		if (_lifetime is not null)
		{
			throw new InvalidOperationException("The WebSocket client is already running.");
		}

		_path = path;
		Volatile.Write(ref _disconnecting, 0);
		_lifetime = new CancellationTokenSource();
		try
		{
			await ConnectSocketAsync(cancellationToken);
			_receiveTask = ReceiveLoopAsync(_lifetime.Token);
			_heartbeatTask = HeartbeatLoopAsync(_lifetime.Token);
		}
		catch
		{
			_lifetime.Dispose();
			_lifetime = null;
			throw;
		}
	}

	public async Task DisconnectAsync(CancellationToken cancellationToken = default)
	{
		var lifetime = Interlocked.Exchange(ref _lifetime, null);
		if (lifetime is null)
		{
			return;
		}

		Volatile.Write(ref _disconnecting, 1);
		var socket = Volatile.Read(ref _socket);
		if (socket?.State is WebSocketState.Open or WebSocketState.CloseReceived)
		{
			try
			{
				await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", cancellationToken);
			}
			catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
			{
			}
		}

		lifetime.Cancel();
		await AwaitLoopAsync(_receiveTask);
		await AwaitLoopAsync(_heartbeatTask);
		DisposeSocket();
		FailPendingAcknowledgements(new OperationCanceledException("WebSocket disconnected."));
		lifetime.Dispose();
		Volatile.Write(ref _disconnecting, 0);
	}

	public async Task SubscribeAsync(string topic, CancellationToken cancellationToken = default)
	{
		ValidateTopic(topic);
		_subscriptions.TryAdd(topic, 0);
		try
		{
			await SendAsync("opx.subscribe", new { topic }, topic, true, cancellationToken);
		}
		catch
		{
			_subscriptions.TryRemove(topic, out _);
			throw;
		}
	}

	public async Task UnsubscribeAsync(string topic, CancellationToken cancellationToken = default)
	{
		ValidateTopic(topic);
		await SendAsync("opx.unsubscribe", new { topic }, topic, true, cancellationToken);
		_subscriptions.TryRemove(topic, out _);
	}

	public async Task<string> SendAsync<T>(
		string type,
		T data,
		string? topic = null,
		bool requireAck = false,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(type))
		{
			throw new ArgumentException("Message type is required.", nameof(type));
		}

		var messageId = Guid.NewGuid().ToString("N");
		TaskCompletionSource<bool>? acknowledgement = null;
		if (requireAck)
		{
			acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			_pendingAcks[messageId] = acknowledgement;
		}

		try
		{
			await SendEnvelopeAsync(type, data, topic, messageId, requireAck, cancellationToken);
			if (acknowledgement is not null)
			{
				var accepted = await acknowledgement.Task.WaitAsync(_options.AckTimeout, cancellationToken);
				if (!accepted)
				{
					throw new InvalidOperationException($"WebSocket message '{messageId}' was rejected by the server.");
				}
			}

			return messageId;
		}
		finally
		{
			if (acknowledgement is not null)
			{
				_pendingAcks.TryRemove(messageId, out _);
			}
		}
	}

	public IAsyncEnumerable<OpxWebSocketMessage> ReadAllAsync(CancellationToken cancellationToken = default)
		=> _incoming.Reader.ReadAllAsync(cancellationToken);

	public OpxWebSocketClientHealth GetHealth()
	{
		return new OpxWebSocketClientHealth(
			State == WebSocketState.Open,
			_connectedAt,
			ReadTimestamp(_lastReceivedUnixMilliseconds),
			ReadTimestamp(_lastSentUnixMilliseconds),
			ReadTimestamp(_lastPongUnixMilliseconds),
			Interlocked.Read(ref _messagesReceived),
			Interlocked.Read(ref _messagesSent),
			Interlocked.Read(ref _reconnects),
			_subscriptions.Count,
			_pendingAcks.Count);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await DisconnectAsync();
		_incoming.Writer.TryComplete();
		_connectLock.Dispose();
		_sendLock.Dispose();
	}

	private async Task ConnectSocketAsync(CancellationToken cancellationToken)
	{
		await _connectLock.WaitAsync(cancellationToken);
		try
		{
			var socket = new ClientWebSocket();
			socket.Options.KeepAliveInterval = _options.KeepAliveInterval;
			foreach (var header in _options.Headers)
			{
				socket.Options.SetRequestHeader(header.Key, header.Value);
			}

			var token = await ResolveTokenAsync(cancellationToken);
			if (!string.IsNullOrWhiteSpace(token))
			{
				socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
			}

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(_options.ConnectTimeout);
			await socket.ConnectAsync(BuildUri(_options.BaseAddress, _path!), timeout.Token);
			var previous = Interlocked.Exchange(ref _socket, socket);
			previous?.Dispose();
			_connectedAt = DateTimeOffset.UtcNow;
			var now = _connectedAt.Value.ToUnixTimeMilliseconds();
			Interlocked.Exchange(ref _lastPongUnixMilliseconds, now);
			Interlocked.Exchange(ref _lastReceivedUnixMilliseconds, now);
			Interlocked.Exchange(ref _lastSentUnixMilliseconds, now);
		}
		finally
		{
			_connectLock.Release();
		}
	}

	private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
	{
		var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1024, _options.ReceiveBufferBytes));
		var messageBuffer = new ArrayBufferWriter<byte>(Math.Max(1024, _options.ReceiveBufferBytes));
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var socket = Volatile.Read(ref _socket);
				if (socket?.State != WebSocketState.Open)
				{
					if (!await ReconnectAsync(cancellationToken))
					{
						break;
					}
					socket = Volatile.Read(ref _socket);
				}

				try
				{
					var result = await socket!.ReceiveAsync(buffer.AsMemory(), cancellationToken);
					if (result.MessageType == WebSocketMessageType.Close)
					{
						messageBuffer.Clear();
						socket.Abort();
						continue;
					}

					if (messageBuffer.WrittenCount + result.Count > _options.MaxMessageBytes)
					{
						socket.Abort();
						throw new InvalidOperationException("Incoming WebSocket message exceeds the configured limit.");
					}

					messageBuffer.Write(buffer.AsSpan(0, result.Count));
					if (!result.EndOfMessage)
					{
						continue;
					}

					if (result.MessageType == WebSocketMessageType.Text)
					{
						await ProcessTextMessageAsync(messageBuffer.WrittenMemory, cancellationToken);
					}
					messageBuffer.Clear();
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
				catch (WebSocketException)
				{
					messageBuffer.Clear();
					socket?.Abort();
				}
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private async Task ProcessTextMessageAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
	{
		OpxWebSocketMessage? message;
		try
		{
			message = JsonSerializer.Deserialize<OpxWebSocketMessage>(payload.Span, JsonOptions);
		}
		catch (JsonException)
		{
			return;
		}

		if (message is null || string.IsNullOrWhiteSpace(message.Type))
		{
			return;
		}

		Interlocked.Exchange(ref _lastReceivedUnixMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
		Interlocked.Increment(ref _messagesReceived);
		if (message.Type.Equals("opx.ack", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(message.CorrelationId)
			&& _pendingAcks.TryGetValue(message.CorrelationId, out var acknowledgement))
		{
			var accepted = message.Data.ValueKind != JsonValueKind.Object
				|| !message.Data.TryGetProperty("accepted", out var acceptedValue)
				|| acceptedValue.ValueKind != JsonValueKind.False;
			acknowledgement.TrySetResult(accepted);
			return;
		}

		if (message.Type.Equals("opx.pong", StringComparison.OrdinalIgnoreCase))
		{
			Interlocked.Exchange(ref _lastPongUnixMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
			return;
		}

		await _incoming.Writer.WriteAsync(message, cancellationToken);
	}

	private async Task<bool> ReconnectAsync(CancellationToken cancellationToken)
	{
		if (!_options.AutoReconnect || Volatile.Read(ref _disconnecting) != 0)
		{
			return false;
		}

		FailPendingAcknowledgements(new WebSocketException("Connection lost before acknowledgement."));
		var attempt = 0;
		while (!cancellationToken.IsCancellationRequested
			&& (_options.MaxReconnectAttempts <= 0 || attempt < _options.MaxReconnectAttempts))
		{
			attempt++;
			var exponential = Math.Min(_options.ReconnectMaxDelay.TotalMilliseconds,
				_options.ReconnectInitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
			var jitter = Random.Shared.NextDouble() * Math.Min(250, exponential * 0.2);
			await Task.Delay(TimeSpan.FromMilliseconds(exponential + jitter), cancellationToken);
			try
			{
				await ConnectSocketAsync(cancellationToken);
				Interlocked.Increment(ref _reconnects);
				await ResubscribeAsync(cancellationToken);
				return true;
			}
			catch (Exception exception) when (exception is WebSocketException or HttpRequestException or OperationCanceledException)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return false;
				}
			}
		}

		return false;
	}

	private async Task ResubscribeAsync(CancellationToken cancellationToken)
	{
		foreach (var topic in _subscriptions.Keys)
		{
			await SendEnvelopeAsync("opx.subscribe", new { topic }, topic, Guid.NewGuid().ToString("N"), false, cancellationToken);
		}
	}

	private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
	{
		if (_options.HeartbeatInterval <= TimeSpan.Zero)
		{
			return;
		}

		using var timer = new PeriodicTimer(_options.HeartbeatInterval);
		while (await timer.WaitForNextTickAsync(cancellationToken))
		{
			var socket = Volatile.Read(ref _socket);
			if (socket?.State != WebSocketState.Open)
			{
				continue;
			}

			var lastPong = ReadTimestamp(Interlocked.Read(ref _lastPongUnixMilliseconds));
			if (_options.HeartbeatTimeout > TimeSpan.Zero && lastPong.HasValue && DateTimeOffset.UtcNow - lastPong.Value > _options.HeartbeatTimeout)
			{
				socket.Abort();
				continue;
			}

			try
			{
				await SendEnvelopeAsync("opx.ping", new { timestamp = DateTimeOffset.UtcNow }, null, Guid.NewGuid().ToString("N"), false, cancellationToken);
			}
			catch (WebSocketException)
			{
				socket.Abort();
			}
		}
	}

	private async Task SendEnvelopeAsync<T>(string type, T data, string? topic, string messageId, bool requireAck, CancellationToken cancellationToken)
	{
		var payload = JsonSerializer.SerializeToUtf8Bytes(new
		{
			type,
			messageId,
			correlationId = (string?)null,
			topic,
			requireAck,
			data
		}, JsonOptions);
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			var socket = Volatile.Read(ref _socket);
			if (socket?.State != WebSocketState.Open)
			{
				throw new WebSocketException(WebSocketError.InvalidState, "WebSocket is not connected.");
			}

			await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
			Interlocked.Exchange(ref _lastSentUnixMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
			Interlocked.Increment(ref _messagesSent);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private async Task<string?> ResolveTokenAsync(CancellationToken cancellationToken)
	{
		if (_tokenProvider is null)
		{
			return null;
		}

		var token = await _tokenProvider.GetTokenAsync(cancellationToken);
		if (token?.ShouldRefresh(DateTimeOffset.UtcNow, _options.RefreshTokenBeforeExpires) == true)
		{
			token = await _tokenProvider.RefreshTokenAsync(cancellationToken);
		}

		return token?.HasAccessToken == true ? token.AccessToken : null;
	}

	private static Uri BuildUri(string baseAddress, string path)
	{
		if (string.IsNullOrWhiteSpace(baseAddress))
		{
			throw new InvalidOperationException("WebSocket BaseAddress is required.");
		}

		var baseUri = new Uri(baseAddress.EndsWith('/') ? baseAddress : $"{baseAddress}/", UriKind.Absolute);
		var builder = new UriBuilder(new Uri(baseUri, path.TrimStart('/')))
		{
			Scheme = baseUri.Scheme switch
			{
				"http" => "ws",
				"https" => "wss",
				"ws" or "wss" => baseUri.Scheme,
				_ => throw new InvalidOperationException("WebSocket BaseAddress must use http, https, ws, or wss.")
			}
		};
		return builder.Uri;
	}

	private static void ValidateTopic(string topic)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			throw new ArgumentException("Topic is required.", nameof(topic));
		}
	}

	private void FailPendingAcknowledgements(Exception exception)
	{
		foreach (var acknowledgement in _pendingAcks)
		{
			if (_pendingAcks.TryRemove(acknowledgement.Key, out var completion))
			{
				completion.TrySetException(exception);
			}
		}
	}

	private void DisposeSocket()
	{
		Interlocked.Exchange(ref _socket, null)?.Dispose();
		_connectedAt = null;
	}

	private static async Task AwaitLoopAsync(Task? task)
	{
		if (task is null)
		{
			return;
		}

		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static DateTimeOffset? ReadTimestamp(long milliseconds)
		=> milliseconds <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
}
