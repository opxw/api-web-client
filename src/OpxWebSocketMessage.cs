// Copyright (c) 2026 - opx
using System.Text.Json;

namespace Opx.Api.Client;

public sealed class OpxWebSocketMessage
{
	public string Type { get; set; } = string.Empty;
	public string MessageId { get; set; } = string.Empty;
	public string? CorrelationId { get; set; }
	public string? Topic { get; set; }
	public bool RequireAck { get; set; }
	public JsonElement Data { get; set; }

	public T? GetData<T>(JsonSerializerOptions? options = null)
	{
		return Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? default : Data.Deserialize<T>(options);
	}
}

public sealed record OpxWebSocketClientHealth(
	bool Connected,
	DateTimeOffset? ConnectedAt,
	DateTimeOffset? LastReceivedAt,
	DateTimeOffset? LastSentAt,
	DateTimeOffset? LastPongAt,
	long MessagesReceived,
	long MessagesSent,
	long Reconnects,
	int Subscriptions,
	int PendingAcknowledgements);
