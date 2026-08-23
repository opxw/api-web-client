# Opx.Api.Client

Typed client wrapper for the `Opx.Api.Web` response contract.

## Features

- `net10.0` only.
- Async and sync API calls.
- `HttpClientFactory` registration.
- Named clients for multiple API sources.
- OPX response wrapper: `OpxApiResult<T>`.
- Result helpers: `EnsureSuccess`, `GetDataOrThrow`, `GetDataOrDefault`.
- Route, query, body, custom header, and bearer token support.
- Lazy bearer token auto-check and refresh.
- Retry once on OPX `401`.
- Automatic per-operation `X-Request-ID`, preserved across an unauthorized retry.
- CancellationToken overloads.
- Fast JSON `data` parsing to `<T>`.
- Configurable OPX body parsing for HTTP `4xx` and `5xx` responses.
- Opt-in structured logging for the executed endpoint and result status code.
- Streaming download for large files without buffering full content in memory.
- Streaming multipart upload with a binary response, including retry-safe stream factories.
- Native WebSocket client with typed messages, topics, ACK, heartbeat, automatic reconnect, and resubscribe.

## Register with HttpClientFactory

```csharp
builder.Services.AddOpxApiClient("http://localhost:5141");
```

Request ID behavior can be configured per client:

```csharp
builder.Services.AddOpxApiClient(options =>
{
    options.BaseAddress = "https://api.server.com";
    options.GenerateRequestId = true;
    options.RequestIdHeaderName = "X-Request-ID";
});
```

To read OPX error content from actual HTTP `401`, `403`, `404`, or `500` responses:

```csharp
builder.Services.AddOpxApiClient(options =>
{
	options.BaseAddress = "https://api.server.com";
	options.ParseErrorResponseBody = true;
});
```

`ParseErrorResponseBody` defaults to `true`. Set it to `false` to use only the HTTP status and reason phrase for non-success responses.

Executed endpoint logging is disabled by default. Enable it per client when the
HTTP method, endpoint path, and resulting OPX/HTTP status code are needed:

```csharp
builder.Services.AddOpxApiClient(options =>
{
	options.BaseAddress = "https://api.server.com";
	options.EnableExecutedEndpointLogging = true;
});
```

The structured Information log includes `HttpMethod`, `ExecutedEndpoint`, and
`ResultStatusCode`. The endpoint excludes its query string, and request headers
and bodies are never logged by this feature.

Usage:

```csharp
public sealed class ArtistService
{
	private readonly IOpxApiClient _client;

	public ArtistService(IOpxApiClient client)
	{
		_client = client;
	}

	public Task<OpxApiResult<List<ArtistDto>>> GetArtists()
	{
		return _client.GetAsync<List<ArtistDto>>("/api/artists");
	}

	public OpxApiResult<ArtistDto> GetArtist(int id)
	{
		return _client.Get<ArtistDto>("/api/artists/{id}", new OpxApiRequest
		{
			FromRoute = new { id }
		});
	}
}
```

## Async and Sync

```csharp
var asyncResult = await client.GetAsync<List<ArtistDto>>("/api/artists");
var syncResult = client.Get<List<ArtistDto>>("/api/artists");

var createdAsync = await client.PostAsync<ArtistDto>("/api/artists", new OpxApiRequest
{
	FromBody = new { name = "DeadSquad" }
});

var createdSync = client.Post<ArtistDto>("/api/artists", new OpxApiRequest
{
	FromBody = new { name = "DeadSquad" }
});
```

## Named Clients

Register multiple API sources:

```csharp
builder.Services.AddOpxApiClient("identity", "https://identity.server.com");
builder.Services.AddOpxApiClient("chinook", "https://chinook.server.com");
```

Use the factory:

```csharp
public sealed class GatewayService
{
	private readonly IOpxApiClientFactory _factory;

	public GatewayService(IOpxApiClientFactory factory)
	{
		_factory = factory;
	}

	public async Task<UserDto> GetUserAsync(CancellationToken cancellationToken)
	{
		var identity = _factory.CreateClient("identity");
		var result = await identity.GetAsync<UserDto>("/api/users/me", null, cancellationToken);
		return result.GetDataOrThrow();
	}
}
```

## Result Helpers

```csharp
var result = await client.GetAsync<ArtistDto>("/api/artists/1");

result.EnsureSuccess();
var artist = result.GetDataOrThrow();
var fallback = result.GetDataOrDefault(new ArtistDto(0, "Unknown"));
```

## CancellationToken

```csharp
var result = await client.GetAsync<ArtistDto>(
	"/api/artists/{id}",
	new OpxApiRequest
	{
		FromRoute = new { id = 1 }
	},
	HttpContext.RequestAborted);
```

## Bearer Token Auto Refresh

Register token provider once:

```csharp
builder.Services.AddOpxInMemoryTokenProvider();
builder.Services.AddOpxApiClient("https://api.server.com");
```

After login, store the token:

```csharp
await tokenProvider.SetTokenAsync(
	loginResult.AccessToken,
	loginResult.RefreshToken,
	loginResult.ExpiresAt);
```

Send device metadata in the login request so the authentication server can
validate it and include the accepted values in the signed shared JWT:

```csharp
var device = OpxClientDeviceMetadata.CreateDesktop(
    installationId,
    "Trust Desktop",
    "5.0.0");

var loginResult = await client.PostAsync<LoginToken>("/auth/login", new OpxApiRequest
{
    FromBody = new
    {
        userName,
        password,
        device
    }
});

var login = loginResult.GetDataOrThrow();
await tokenProvider.SetTokenAsync(
    login.AccessToken,
    login.RefreshToken,
    login.ExpiresAt);
```

Use `CreateMobile` for a mobile installation and `CreateWeb` for a browser
session. Persist `installationId` securely; do not generate a new ID for every
request or use a MAC address/hardware serial. Each device session should
receive a different token, while all tokens share the same issuer and claim
schema.

Requests will automatically attach the bearer token:

```csharp
var result = await client.GetAsync<UserDto>("/api/users/me");
```

If the token is close to expiry, the provider refreshes before sending the request. If the API returns OPX status code `401` in either an HTTP 200 wrapper or an actual HTTP 401 response, the client refreshes once and retries once.

Manual bearer token still works and takes priority:

```csharp
var result = await client.GetAsync<UserDto>("/api/users/me", new OpxApiRequest
{
	BearerToken = token
});
```

Override the generated request ID when an upstream operation already has one:

```csharp
var result = await client.GetAsync<UserDto>("/api/users/me", new OpxApiRequest
{
    RequestId = upstreamRequestId
});
```

## Streaming Download

Use `DownloadAsync` for large files such as updater ZIP packages:

```csharp
await using var file = File.Create("update.zip");
var progress = new Progress<OpxDownloadProgress>(value =>
{
	if (value.Percent.HasValue)
	{
		Console.WriteLine($"{value.Percent.Value:N2}%");
	}
});

var result = await client.DownloadAsync(
	"/api/updates/{version}/package",
	file,
	new OpxApiRequest
	{
		FromRoute = new { version = "1.0.7" },
		Headers = new Dictionary<string, string?>
		{
			["X-Updater"] = "opx"
		}
	},
	progress,
	cancellationToken);
```

`DownloadAsync` uses `HttpCompletionOption.ResponseHeadersRead`, writes directly to the destination stream, supports custom headers and bearer token auto-refresh, and retries once on HTTP `401`.

## Multipart Upload With Streaming Response

Use `PostDownloadAsync` when a file upload returns a binary artifact. Input and
output remain stream-based, so large documents do not need Base64 or full-memory
buffering. `OpenReadStream` is a factory because an authentication retry must
open a fresh source stream.

```csharp
await using var destination = File.Create("report.pdf");
var result = await client.PostDownloadAsync(
	"api/v1/document-conversions",
	destination,
	new OpxApiRequest
	{
		BearerToken = token,
		FromMultipart = new OpxMultipartFormData
		{
			Fields = new Dictionary<string, string?>
			{
				["targetFormat"] = "Pdf"
			},
			Files =
			[
				new OpxMultipartFile
				{
					Name = "file",
					FileName = "report.docx",
					ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
					OpenReadStream = () => File.OpenRead("report.docx")
				}
			]
		}
	},
	cancellationToken: cancellationToken);
```

## WebSocket Client

Register a default or named realtime source:

```csharp
builder.Services.AddOpxInMemoryTokenProvider();
builder.Services.AddOpxApiWebSocketClient("chinook-realtime", "https://api.server.com", options =>
{
	options.AutoReconnect = true;
	options.HeartbeatInterval = TimeSpan.FromSeconds(30);
	options.AckTimeout = TimeSpan.FromSeconds(10);
});
```

Create one stateful client for the consumer lifetime, connect, and subscribe:

```csharp
await using var realtime = webSocketFactory.CreateClient("chinook-realtime");
await realtime.ConnectAsync("/ws/chinook", cancellationToken);
await realtime.SubscribeAsync("artists", cancellationToken);

var messageId = await realtime.SendAsync(
	"artist.watch",
	new { artistId = 1 },
	requireAck: true,
	cancellationToken: cancellationToken);

await foreach (var message in realtime.ReadAllAsync(cancellationToken))
{
	if (message.Type == "artist.updated")
	{
		var artist = message.GetData<ArtistDto>();
	}
}
```

The client uses the registered `IOpxTokenProvider` before the initial connection and each reconnect. It reconnects with exponential backoff and jitter, then restores all topic subscriptions. Application messages are not replayed automatically, preventing duplicate writes; use `requireAck` and retain the returned message ID when business-level retry is needed.

Connection health is available without network I/O:

```csharp
var health = realtime.GetHealth();
Console.WriteLine($"connected={health.Connected}, reconnects={health.Reconnects}, pong={health.LastPongAt}");
```

## Chinook Sample

Run the Chinook API first, then:

```powershell
$env:CHINOOK_API_BASE_URL="http://localhost:5141"
dotnet run --project .\sample\ChinookClientSample\ChinookClientSample.csproj
```

Example output:

```text
GET /api/artists => result=True, statusCode=200, count=275
GET /api/artists/1 => result=True, name=AC/DC
SYNC GET /api/artists/2 => result=True, name=Accept
GET /api/artists/with-albums => result=True, count=275
WS artist.watch => ack=<message-id>, artistId=1, reconnects=0
```

## Test

```powershell
dotnet test .\test\Opx.Api.Client.Tests.csproj
```

Run stress test with a live Chinook API:

```powershell
$env:OPX_STRESS_BASE_URL="http://127.0.0.1:5141"
dotnet test .\test\Opx.Api.Client.Tests.csproj --filter "FullyQualifiedName~OpxApiClientStressTests" --logger "console;verbosity=detailed"
```

Recent smoke result on this PC:

```text
500 concurrent requests without token provider: 667 ms, 1.33 ms/request
500 concurrent requests with token provider: 232 ms, 0.46 ms/request
```

Run parse performance test:

```powershell
dotnet test .\test\Opx.Api.Client.Tests.csproj --filter "FullyQualifiedName~OpxApiClientParsePerformanceTests" --logger "console;verbosity=detailed"
```

Recent parse result:

```text
Payload artists: 1000
Iterations: 200
Legacy JsonElement->T: 1307 ms
Fast data->T: 569 ms
Delta: 738 ms
Opx WebSocket client 500 acknowledged messages: 253 ms
```

## Release Pack

```powershell
.\pack-release.ps1
```

Set version manually:

```powershell
.\pack-release.ps1 -VersionPrefix "1.0.1"
```
