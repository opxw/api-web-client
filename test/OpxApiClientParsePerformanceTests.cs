// Copyright (c) 2026 - opx
using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace Opx.Api.Client.Tests;

[TestFixture]
public class OpxApiClientParsePerformanceTests
{
	[Test]
	public void ParseLargeResponse_CompareLegacyAndFastPath_WritesElapsedTime()
	{
		const int iterations = 200;
		var json = CreateArtistsJson(1000);
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};

		var legacyWarmup = ParseLegacy(json, options);
		var fastWarmup = ParseFast(json, options);
		Assert.That(legacyWarmup, Has.Count.EqualTo(1000));
		Assert.That(fastWarmup, Has.Count.EqualTo(1000));

		var legacy = Stopwatch.StartNew();
		for (var index = 0; index < iterations; index++)
		{
			ParseLegacy(json, options);
		}
		legacy.Stop();

		var fast = Stopwatch.StartNew();
		for (var index = 0; index < iterations; index++)
		{
			ParseFast(json, options);
		}
		fast.Stop();

		TestContext.Out.WriteLine($"Payload artists: 1000");
		TestContext.Out.WriteLine($"Iterations: {iterations}");
		TestContext.Out.WriteLine($"Legacy JsonElement->T: {legacy.ElapsedMilliseconds} ms");
		TestContext.Out.WriteLine($"Fast data->T: {fast.ElapsedMilliseconds} ms");
		TestContext.Out.WriteLine($"Delta: {legacy.ElapsedMilliseconds - fast.ElapsedMilliseconds} ms");

		Assert.That(fastWarmup[0].Name, Is.EqualTo("Artist 1"));
	}

	private static List<ArtistDto> ParseLegacy(string json, JsonSerializerOptions options)
	{
		var response = JsonSerializer.Deserialize<OpxApiResponse<JsonElement>>(json, options);
		return response?.Data.Deserialize<List<ArtistDto>>(options) ?? [];
	}

	private static List<ArtistDto> ParseFast(string json, JsonSerializerOptions options)
	{
		using var document = JsonDocument.Parse(json);
		var data = document.RootElement.GetProperty("data");
		return data.Deserialize<List<ArtistDto>>(options) ?? [];
	}

	private static string CreateArtistsJson(int count)
	{
		var artists = Enumerable
			.Range(1, count)
			.Select(id => new ArtistDto(id, $"Artist {id}", $"Genre {id % 10}", id * 2))
			.ToArray();

		return JsonSerializer.Serialize(new
		{
			result = true,
			data = artists,
			statusCode = "200"
		});
	}

	private sealed record ArtistDto(int ArtistId, string Name, string Genre, int AlbumCount);
}
