// Copyright (c) 2026 - opx
using System.Diagnostics;
using NUnit.Framework;

namespace Opx.Api.Client.Tests;

[TestFixture]
public class OpxApiClientStressTests
{
	[Test]
	public async Task GetAsync_WithFiveHundredConcurrentRequests_WritesElapsedTime()
	{
		var baseUrl = Environment.GetEnvironmentVariable("OPX_STRESS_BASE_URL");
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			Assert.Ignore("Set OPX_STRESS_BASE_URL to run the API client stress test.");
		}

		const int requestCount = 500;
		using var client = new OpxApiClient(baseUrl);

		var warmup = await client.GetAsync<ArtistDto>("/api/artists/1");
		Assert.That(warmup.IsSuccess, Is.True, warmup.Message);

		var stopwatch = Stopwatch.StartNew();
		var tasks = Enumerable
			.Range(0, requestCount)
			.Select(_ => client.GetAsync<ArtistDto>("/api/artists/1"))
			.ToArray();

		var results = await Task.WhenAll(tasks);
		stopwatch.Stop();

		var success = results.Count(result => result.IsSuccess);
		var failed = requestCount - success;
		var averageMs = stopwatch.Elapsed.TotalMilliseconds / requestCount;

		TestContext.Out.WriteLine($"Requests: {requestCount}");
		TestContext.Out.WriteLine($"Success: {success}");
		TestContext.Out.WriteLine($"Failed: {failed}");
		TestContext.Out.WriteLine($"Elapsed: {stopwatch.ElapsedMilliseconds} ms");
		TestContext.Out.WriteLine($"Average: {averageMs:N2} ms/request");

		Assert.Multiple(() =>
		{
			Assert.That(success, Is.EqualTo(requestCount));
			Assert.That(failed, Is.Zero);
		});
	}

	[Test]
	public async Task GetAsync_WithTokenProviderAndFiveHundredConcurrentRequests_WritesElapsedTime()
	{
		var baseUrl = Environment.GetEnvironmentVariable("OPX_STRESS_BASE_URL");
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			Assert.Ignore("Set OPX_STRESS_BASE_URL to run the API client stress test.");
		}

		const int requestCount = 500;
		var tokenProvider = new OpxInMemoryTokenProvider();
		await tokenProvider.SetTokenAsync("stress-token", expiresAt: DateTimeOffset.UtcNow.AddMinutes(15));
		using var client = new OpxApiClient(baseUrl, tokenProvider: tokenProvider);

		var warmup = await client.GetAsync<ArtistDto>("/api/artists/1");
		Assert.That(warmup.IsSuccess, Is.True, warmup.Message);

		var stopwatch = Stopwatch.StartNew();
		var tasks = Enumerable
			.Range(0, requestCount)
			.Select(_ => client.GetAsync<ArtistDto>("/api/artists/1"))
			.ToArray();

		var results = await Task.WhenAll(tasks);
		stopwatch.Stop();

		var success = results.Count(result => result.IsSuccess);
		var failed = requestCount - success;
		var averageMs = stopwatch.Elapsed.TotalMilliseconds / requestCount;

		TestContext.Out.WriteLine($"Requests: {requestCount}");
		TestContext.Out.WriteLine($"Success: {success}");
		TestContext.Out.WriteLine($"Failed: {failed}");
		TestContext.Out.WriteLine($"Elapsed: {stopwatch.ElapsedMilliseconds} ms");
		TestContext.Out.WriteLine($"Average: {averageMs:N2} ms/request");

		Assert.Multiple(() =>
		{
			Assert.That(success, Is.EqualTo(requestCount));
			Assert.That(failed, Is.Zero);
		});
	}

	private sealed record ArtistDto(int ArtistId, string Name);
}
