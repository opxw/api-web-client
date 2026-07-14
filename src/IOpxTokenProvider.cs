// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public interface IOpxTokenProvider
{
	Task SetTokenAsync(
		string accessToken,
		string? refreshToken = null,
		DateTimeOffset? expiresAt = null,
		CancellationToken cancellationToken = default);

	Task<OpxTokenState?> GetTokenAsync(CancellationToken cancellationToken = default);
	Task<OpxTokenState?> RefreshTokenAsync(CancellationToken cancellationToken = default);
	Task ClearAsync(CancellationToken cancellationToken = default);
}
