// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public sealed class OpxTokenState
{
	public string AccessToken { get; init; } = string.Empty;
	public string? RefreshToken { get; init; }
	public DateTimeOffset? ExpiresAt { get; init; }

	public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);
	public bool IsExpired(DateTimeOffset utcNow)
	{
		return ExpiresAt.HasValue && utcNow >= ExpiresAt.Value;
	}

	public bool ShouldRefresh(DateTimeOffset utcNow, TimeSpan refreshBeforeExpires)
	{
		return ExpiresAt.HasValue && utcNow >= ExpiresAt.Value.Subtract(refreshBeforeExpires);
	}
}
