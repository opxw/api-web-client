// Copyright (c) 2026 - opx
using Microsoft.Extensions.DependencyInjection;
using Opx.Api.Client;

var baseUrl = Environment.GetEnvironmentVariable("CHINOOK_API_BASE_URL") ?? "http://localhost:5141";

var services = new ServiceCollection();
services.AddOpxApiClient("chinook", baseUrl);

await using var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<IOpxApiClientFactory>();
var client = factory.CreateClient("chinook");
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var artistsResult = await client.GetAsync<List<ArtistDto>>("/api/artists", null, cancellation.Token);
var artists = artistsResult.GetDataOrDefault([]);
Console.WriteLine($"GET /api/artists => result={artistsResult.Result}, statusCode={artistsResult.StatusCode}, count={artists?.Count ?? 0}");

var artistResult = await client.GetAsync<ArtistDto>("/api/artists/{id}", new OpxApiRequest
{
	FromRoute = new { id = 1 }
}, cancellation.Token);
var artist = artistResult.GetDataOrThrow();
Console.WriteLine($"GET /api/artists/1 => result={artistResult.Result}, name={artist.Name}");

var syncArtist = client.Get<ArtistDto>("/api/artists/{id}", new OpxApiRequest
{
	FromRoute = new { id = 2 }
});
Console.WriteLine($"SYNC GET /api/artists/2 => result={syncArtist.Result}, name={syncArtist.Data?.Name}");

var artistsWithAlbums = await client.GetAsync<List<ArtistWithAlbumsDto>>("/api/artists/with-albums");
Console.WriteLine($"GET /api/artists/with-albums => result={artistsWithAlbums.Result}, count={artistsWithAlbums.Data?.Count ?? 0}");

public sealed record ArtistDto(int ArtistId, string Name);
public sealed record AlbumDto(int AlbumId, string Title);
public sealed record ArtistWithAlbumsDto(int ArtistId, string Name, List<AlbumDto> Albums);
