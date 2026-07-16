// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public sealed class OpxWebSocketClientOptions
{
	public string BaseAddress { get; set; } = string.Empty;
	public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);
	public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(20);
	public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
	public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(90);
	public TimeSpan AckTimeout { get; set; } = TimeSpan.FromSeconds(10);
	public TimeSpan RefreshTokenBeforeExpires { get; set; } = TimeSpan.FromSeconds(60);
	public TimeSpan ReconnectInitialDelay { get; set; } = TimeSpan.FromSeconds(1);
	public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);
	public bool AutoReconnect { get; set; } = true;
	public int MaxReconnectAttempts { get; set; }
	public int ReceiveBufferBytes { get; set; } = 16 * 1024;
	public int MaxMessageBytes { get; set; } = 1024 * 1024;
	public int IncomingMessageCapacity { get; set; } = 1024;
	public Dictionary<string, string> Headers { get; set; } = [];
}

internal sealed record OpxNamedWebSocketClientOptions(string Name, OpxWebSocketClientOptions Options);
