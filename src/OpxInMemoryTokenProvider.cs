// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public sealed class OpxInMemoryTokenProvider : IOpxTokenProvider
{
	private readonly SemaphoreSlim _refreshSync = new(1, 1);
	private OpxTokenState? _token;

	public Func<OpxTokenState?, CancellationToken, Task<OpxTokenState?>>? RefreshAsync { get; set; }

	public async Task SetTokenAsync(
		string accessToken,
		string? refreshToken = null,
		DateTimeOffset? expiresAt = null,
		CancellationToken cancellationToken = default)
	{
		Volatile.Write(ref _token, new OpxTokenState
		{
			AccessToken = accessToken,
			RefreshToken = refreshToken,
			ExpiresAt = expiresAt
		});
		await Task.CompletedTask;
	}

	public Task<OpxTokenState?> GetTokenAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(Volatile.Read(ref _token));
	}

	public async Task<OpxTokenState?> RefreshTokenAsync(CancellationToken cancellationToken = default)
	{
		await _refreshSync.WaitAsync(cancellationToken);
		try
		{
			if (RefreshAsync is null)
			{
				return Volatile.Read(ref _token);
			}

			var token = await RefreshAsync(Volatile.Read(ref _token), cancellationToken);
			Volatile.Write(ref _token, token);
			return token;
		}
		finally
		{
			_refreshSync.Release();
		}
	}

	public Task ClearAsync(CancellationToken cancellationToken = default)
	{
		Volatile.Write(ref _token, null);
		return Task.CompletedTask;
	}
}
