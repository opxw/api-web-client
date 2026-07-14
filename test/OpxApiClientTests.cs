// Copyright (c) 2026 - opx
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opx.Api.Client;

namespace Opx.Api.Client.Tests;

[TestFixture]
public class OpxApiClientTests
{
	[Test]
	public async Task GetAsync_WhenResponseIsOpxArray_ReturnsTypedData()
	{
		var handler = new StubHttpMessageHandler(_ => JsonResponse("""
			{
			  "result": true,
			  "data": [
			    { "artistId": 1, "name": "DeadSquad" },
			    { "artistId": 2, "name": "Burgerkill" }
			  ],
			  "statusCode": "200"
			}
			"""));
		using var client = new OpxApiClient("https://chinook.local", handler: handler);

		var result = await client.GetAsync<List<ArtistDto>>("/api/artists");

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.Data, Has.Count.EqualTo(2));
			Assert.That(result.Data![0].Name, Is.EqualTo("DeadSquad"));
			Assert.That(handler.LastRequest?.RequestUri?.ToString(), Is.EqualTo("https://chinook.local/api/artists"));
		});
	}

	[Test]
	public async Task GetAsync_WhenRouteAndQueryProvided_BuildsChinookArtistUrl()
	{
		var handler = new StubHttpMessageHandler(_ => JsonResponse("""
			{
			  "result": true,
			  "data": { "artistId": 1, "name": "DeadSquad" },
			  "statusCode": "200"
			}
			"""));
		using var client = new OpxApiClient("https://chinook.local", handler: handler);

		var result = await client.GetAsync<ArtistDto>("/api/artists/{id}", new OpxApiRequest
		{
			FromRoute = new { id = 1 },
			FromQuery = new { includeAlbums = true }
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Data?.ArtistId, Is.EqualTo(1));
			Assert.That(handler.LastRequest?.RequestUri?.ToString(), Is.EqualTo("https://chinook.local/api/artists/1?includeAlbums=True"));
		});
	}

	[Test]
	public async Task PostAsync_WhenBearerHeaderAndBodyProvided_SendsJsonRequest()
	{
		var handler = new StubHttpMessageHandler(_ => JsonResponse("""
			{
			  "result": true,
			  "data": { "artistId": 3, "name": "Seringai" },
			  "statusCode": "200"
			}
			"""));
		using var client = new OpxApiClient("https://chinook.local", handler: handler);

		var result = await client.PostAsync<ArtistDto>("/api/artists", new OpxApiRequest
		{
			BearerToken = "jwt-token",
			Headers = new Dictionary<string, string?> { ["X-Client"] = "opx" },
			FromBody = new { name = "Seringai" }
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Data?.Name, Is.EqualTo("Seringai"));
			Assert.That(handler.LastRequest?.Headers.Authorization?.Scheme, Is.EqualTo("Bearer"));
			Assert.That(handler.LastRequest?.Headers.Authorization?.Parameter, Is.EqualTo("jwt-token"));
			Assert.That(handler.LastRequest?.Headers.GetValues("X-Client").Single(), Is.EqualTo("opx"));
			Assert.That(handler.LastRequestBody, Does.Contain("Seringai"));
		});
	}

	[Test]
	public async Task GetAsync_WhenOpxErrorResponse_ReturnsMessageAndStatusCode()
	{
		var handler = new StubHttpMessageHandler(_ => JsonResponse("""
			{
			  "result": false,
			  "data": { "message": "Not found", "id": "Artist", "objectName": "api/artists/404" },
			  "statusCode": "404"
			}
			"""));
		using var client = new OpxApiClient("https://chinook.local", handler: handler);

		var result = await client.GetAsync<ArtistDto>("/api/artists/404");

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSuccess, Is.False);
			Assert.That(result.Result, Is.False);
			Assert.That(result.StatusCode, Is.EqualTo("404"));
			Assert.That(result.Message, Is.EqualTo("Not found"));
		});
	}

	[Test]
	public async Task HttpClientFactory_WhenRegistered_ResolvesTypedOpxApiClient()
	{
		var services = new ServiceCollection();
		services
			.AddOpxApiClient("https://chinook.local")
			.ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ => JsonResponse("""
				{
				  "result": true,
				  "data": [
				    { "artistId": 1, "name": "DeadSquad" }
				  ],
				  "statusCode": "200"
				}
				""")));
		await using var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<IOpxApiClient>();

		var result = await client.GetAsync<List<ArtistDto>>("/api/artists");

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.Data?.Single().Name, Is.EqualTo("DeadSquad"));
		});
	}

	[Test]
	public async Task NamedClientFactory_WhenRegistered_ResolvesClientByName()
	{
		var services = new ServiceCollection();
		services
			.AddOpxApiClient("identity", "https://identity.local")
			.ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ => JsonResponse("""
				{
				  "result": true,
				  "data": { "artistId": 1, "name": "Identity" },
				  "statusCode": "200"
				}
				""")));
		services
			.AddOpxApiClient("chinook", "https://chinook.local")
			.ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ => JsonResponse("""
				{
				  "result": true,
				  "data": { "artistId": 2, "name": "Chinook" },
				  "statusCode": "200"
				}
				""")));
		await using var provider = services.BuildServiceProvider();
		var factory = provider.GetRequiredService<IOpxApiClientFactory>();

		var identity = await factory.CreateClient("identity").GetAsync<ArtistDto>("/api/source");
		var chinook = await factory.CreateClient("chinook").GetAsync<ArtistDto>("/api/source");

		Assert.Multiple(() =>
		{
			Assert.That(identity.Data?.Name, Is.EqualTo("Identity"));
			Assert.That(chinook.Data?.Name, Is.EqualTo("Chinook"));
		});
	}

	[Test]
	public void SyncMethods_WhenCalled_UseExpectedHttpMethods()
	{
		var methods = new List<HttpMethod>();
		var handler = new StubHttpMessageHandler(request =>
		{
			methods.Add(request.Method);
			return JsonResponse("""
				{
				  "result": true,
				  "data": true,
				  "statusCode": "200"
				}
				""");
		});
		using var client = new OpxApiClient("https://chinook.local", handler: handler);

		var getResult = client.Get<bool>("/api/ping");
		var postResult = client.Post<bool>("/api/ping");
		var putResult = client.Put<bool>("/api/ping");
		var deleteResult = client.Delete<bool>("/api/ping");

		Assert.Multiple(() =>
		{
			Assert.That(getResult.IsSuccess, Is.True);
			Assert.That(postResult.IsSuccess, Is.True);
			Assert.That(putResult.IsSuccess, Is.True);
			Assert.That(deleteResult.IsSuccess, Is.True);
			Assert.That(methods, Is.EqualTo(new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete }));
		});
	}

	[Test]
	public async Task GetAsync_WhenTokenProviderHasToken_AttachesBearerToken()
	{
		var tokenProvider = new OpxInMemoryTokenProvider();
		await tokenProvider.SetTokenAsync("auto-token", expiresAt: DateTimeOffset.UtcNow.AddMinutes(10));
		var handler = new StubHttpMessageHandler(_ => JsonResponse("""
			{
			  "result": true,
			  "data": { "artistId": 1, "name": "DeadSquad" },
			  "statusCode": "200"
			}
			"""));
		using var client = new OpxApiClient("https://chinook.local", handler: handler, tokenProvider: tokenProvider);

		var result = await client.GetAsync<ArtistDto>("/api/artists/1");

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(handler.LastRequest?.Headers.Authorization?.Scheme, Is.EqualTo("Bearer"));
			Assert.That(handler.LastRequest?.Headers.Authorization?.Parameter, Is.EqualTo("auto-token"));
		});
	}

	[Test]
	public async Task GetAsync_WhenTokenAlmostExpired_RefreshesBeforeRequest()
	{
		var tokenProvider = new OpxInMemoryTokenProvider
		{
			RefreshAsync = (_, _) => Task.FromResult<OpxTokenState?>(new OpxTokenState
			{
				AccessToken = "refreshed-token",
				RefreshToken = "refresh-token",
				ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
			})
		};
		await tokenProvider.SetTokenAsync("old-token", "refresh-token", DateTimeOffset.UtcNow.AddSeconds(10));
		var handler = new StubHttpMessageHandler(_ => JsonResponse("""
			{
			  "result": true,
			  "data": { "artistId": 1, "name": "DeadSquad" },
			  "statusCode": "200"
			}
			"""));
		using var client = new OpxApiClient(
			"https://chinook.local",
			handler: handler,
			tokenProvider: tokenProvider,
			options: new OpxApiClientOptions { RefreshTokenBeforeExpires = TimeSpan.FromSeconds(60) });

		var result = await client.GetAsync<ArtistDto>("/api/artists/1");

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(handler.LastRequest?.Headers.Authorization?.Parameter, Is.EqualTo("refreshed-token"));
		});
	}

	[Test]
	public async Task GetAsync_WhenUnauthorized_RefreshesAndRetriesOnce()
	{
		var tokenProvider = new OpxInMemoryTokenProvider
		{
			RefreshAsync = (_, _) => Task.FromResult<OpxTokenState?>(new OpxTokenState
			{
				AccessToken = "retry-token",
				RefreshToken = "refresh-token",
				ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
			})
		};
		await tokenProvider.SetTokenAsync("old-token", "refresh-token", DateTimeOffset.UtcNow.AddMinutes(10));
		var tokens = new List<string?>();
		var call = 0;
		var handler = new StubHttpMessageHandler(request =>
		{
			tokens.Add(request.Headers.Authorization?.Parameter);
			call++;

			return call == 1
				? JsonResponse("""
					{
					  "result": false,
					  "data": { "message": "Unauthorized" },
					  "statusCode": "401"
					}
					""")
				: JsonResponse("""
					{
					  "result": true,
					  "data": { "artistId": 1, "name": "DeadSquad" },
					  "statusCode": "200"
					}
					""");
		});
		using var client = new OpxApiClient("https://chinook.local", handler: handler, tokenProvider: tokenProvider);

		var result = await client.GetAsync<ArtistDto>("/api/artists/1");

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(call, Is.EqualTo(2));
			Assert.That(tokens, Is.EqualTo(new[] { "old-token", "retry-token" }));
		});
	}

	[Test]
	public void GetAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
	{
		var handler = new StubHttpMessageHandler(_ => JsonResponse("""
			{
			  "result": true,
			  "data": { "artistId": 1, "name": "DeadSquad" },
			  "statusCode": "200"
			}
			"""));
		using var client = new OpxApiClient("https://chinook.local", handler: handler);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		Assert.That(async () => await client.GetAsync<ArtistDto>("/api/artists/1", null, cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	[Test]
	public void OpxApiResultHelpers_WhenFailed_ThrowTypedException()
	{
		var result = OpxApiResult<ArtistDto>.Fail("Not found", "404");

		var exception = Assert.Throws<OpxApiClientException>(() => result.EnsureSuccess());

		Assert.Multiple(() =>
		{
			Assert.That(exception?.Message, Is.EqualTo("Not found"));
			Assert.That(exception?.StatusCode, Is.EqualTo("404"));
			Assert.That(result.GetDataOrDefault(new ArtistDto(0, "Default"))?.Name, Is.EqualTo("Default"));
		});
	}

	private static HttpResponseMessage JsonResponse(string json)
	{
		return new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		};
	}

	private sealed class StubHttpMessageHandler : HttpMessageHandler
	{
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

		public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
		{
			_handler = handler;
		}

		public HttpRequestMessage? LastRequest { get; private set; }
		public string? LastRequestBody { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
			}

			LastRequest = request;
			LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
			return Task.FromResult(_handler(request));
		}
	}

	private sealed record ArtistDto(int ArtistId, string Name);
}
