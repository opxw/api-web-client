// Copyright (c) 2026 - opx
using Microsoft.Extensions.DependencyInjection;
using Opx.Api.Client;

var baseUrl = Environment.GetEnvironmentVariable("CHINOOK_API_BASE_URL") ?? "http://localhost:5141";

var services = new ServiceCollection();
services.AddOpxApiClient("chinook", configure: options =>
{
	options.BaseAddress = baseUrl;
	options.ParseErrorResponseBody = true;
	options.GenerateRequestId = true;
	options.RequestIdHeaderName = "X-Request-ID";
});
services.AddOpxApiWebSocketClient("chinook-realtime", baseUrl, options =>
{
	options.HeartbeatInterval = TimeSpan.FromSeconds(30);
	options.AutoReconnect = true;
});

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

await using var realtime = provider.GetRequiredService<IOpxWebSocketClientFactory>().CreateClient("chinook-realtime");
await realtime.ConnectAsync("/ws/chinook", cancellation.Token);
await realtime.SubscribeAsync("artists", cancellation.Token);
var messageId = await realtime.SendAsync("artist.watch", new { artistId = 1 }, requireAck: true, cancellationToken: cancellation.Token);

await foreach (var message in realtime.ReadAllAsync(cancellation.Token))
{
	if (!message.Type.Equals("artist.watching", StringComparison.OrdinalIgnoreCase))
	{
		continue;
	}

	Console.WriteLine($"WS artist.watch => ack={messageId}, artistId={message.GetData<ArtistWatchDto>()?.ArtistId}, reconnects={realtime.GetHealth().Reconnects}");
	break;
}

public sealed record ArtistDto(int ArtistId, string Name);
public sealed record AlbumDto(int AlbumId, string Title);
public sealed record ArtistWithAlbumsDto(int ArtistId, string Name, List<AlbumDto> Albums);
public sealed record ArtistWatchDto(int ArtistId);
