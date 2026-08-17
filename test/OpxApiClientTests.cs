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
	public async Task GetAsync_WhenHttpUnauthorizedContainsOpxResponse_ParsesBody()
	{
		var handler = new StubHttpMessageHandler(_ => JsonResponse("""
			{
			  "result": false,
			  "data": { "message": "Token expired" },
			  "statusCode": "401"
			}
			""", HttpStatusCode.Unauthorized));
		using var client = new OpxApiClient("https://chinook.local", handler: handler);

		var result = await client.GetAsync<ArtistDto>("/api/artists");

		Assert.Multiple(() =>
		{
			Assert.That(result.Result, Is.False);
			Assert.That(result.IsSuccess, Is.False);
			Assert.That(result.StatusCode, Is.EqualTo("401"));
			Assert.That(result.Message, Is.EqualTo("Token expired"));
		});
	}

	[Test]
	public async Task GetAsync_WhenErrorBodyParsingDisabled_UsesHttpReasonPhrase()
	{
		var handler = new StubHttpMessageHandler(_ => JsonResponse("""
			{
			  "result": false,
			  "data": { "message": "Token expired" },
			  "statusCode": "401"
			}
			""", HttpStatusCode.Unauthorized));
		using var client = new OpxApiClient(
			"https://chinook.local",
			handler: handler,
			options: new OpxApiClientOptions { ParseErrorResponseBody = false });

		var result = await client.GetAsync<ArtistDto>("/api/artists");

		Assert.Multiple(() =>
		{
			Assert.That(result.Result, Is.False);
			Assert.That(result.StatusCode, Is.EqualTo("401"));
			Assert.That(result.Message, Is.EqualTo("Unauthorized"));
		});
	}

	[Test]
	public async Task GetAsync_WhenHttpErrorBodyIsNotJson_UsesHttpReasonPhrase()
	{
		var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
		{
			Content = new StringContent("upstream unavailable", Encoding.UTF8, "text/plain")
		});
		using var client = new OpxApiClient("https://chinook.local", handler: handler);

		var result = await client.GetAsync<ArtistDto>("/api/artists");

		Assert.Multiple(() =>
		{
			Assert.That(result.Result, Is.False);
			Assert.That(result.StatusCode, Is.EqualTo("502"));
			Assert.That(result.Message, Is.EqualTo("Bad Gateway"));
		});
	}

	[Test]
	public async Task GetAsync_GeneratesUniqueRequestIdForEachOperation()
	{
		var requestIds = new List<string?>();
		var generatedIds = new Queue<string>(["request-1", "request-2"]);
		var handler = new StubHttpMessageHandler(request =>
		{
			requestIds.Add(request.Headers.GetValues("X-Request-ID").Single());
			return JsonResponse("""
				{
				  "result": true,
				  "data": true,
				  "statusCode": "200"
				}
				""");
		});
		using var client = new OpxApiClient(
			"https://chinook.local",
			handler: handler,
			options: new OpxApiClientOptions
			{
				RequestIdFactory = generatedIds.Dequeue
			});

		await client.GetAsync<bool>("/api/ping");
		await client.GetAsync<bool>("/api/ping");

		Assert.That(requestIds, Is.EqualTo(new[] { "request-1", "request-2" }));
	}

	[Test]
	public async Task GetAsync_WhenRequestIdIsProvided_UsesExplicitValue()
	{
		string? requestId = null;
		var handler = new StubHttpMessageHandler(request =>
		{
			requestId = request.Headers.GetValues("X-Request-ID").Single();
			return JsonResponse("""
				{
				  "result": true,
				  "data": true,
				  "statusCode": "200"
				}
				""");
		});
		using var client = new OpxApiClient("https://chinook.local", handler: handler);

		await client.GetAsync<bool>("/api/ping", new OpxApiRequest
		{
			RequestId = "caller-request-71"
		});

		Assert.That(requestId, Is.EqualTo("caller-request-71"));
	}

	[Test]
	public async Task GetAsync_WhenRequestIdGenerationIsDisabled_DoesNotWriteHeader()
	{
		var hasRequestId = true;
		var handler = new StubHttpMessageHandler(request =>
		{
			hasRequestId = request.Headers.Contains("X-Request-ID");
			return JsonResponse("""
				{
				  "result": true,
				  "data": true,
				  "statusCode": "200"
				}
				""");
		});
		using var client = new OpxApiClient(
			"https://chinook.local",
			handler: handler,
			options: new OpxApiClientOptions { GenerateRequestId = false });

		await client.GetAsync<bool>("/api/ping");

		Assert.That(hasRequestId, Is.False);
	}

	[Test]
	public void OpxClientDeviceMetadata_FactoriesCreateExpectedDeviceTypes()
	{
		var desktop = OpxClientDeviceMetadata.CreateDesktop("desktop-1", "Trust Desktop", "5.0.0");
		var mobile = OpxClientDeviceMetadata.CreateMobile("mobile-1", "Trust Mobile", "3.2.1", "OPX Phone", "Android");
		var web = OpxClientDeviceMetadata.CreateWeb("browser-1", "Trust Web", "2.0.0", "Chrome/Windows");

		Assert.Multiple(() =>
		{
			Assert.That(desktop.DeviceType, Is.EqualTo(OpxClientDeviceTypes.Desktop));
			Assert.That(desktop.DeviceName, Is.Not.Empty);
			Assert.That(mobile.DeviceType, Is.EqualTo(OpxClientDeviceTypes.Mobile));
			Assert.That(mobile.Platform, Is.EqualTo("Android"));
			Assert.That(web.DeviceType, Is.EqualTo(OpxClientDeviceTypes.Web));
			Assert.That(web.DeviceId, Is.EqualTo("browser-1"));
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
		var requestIds = new List<string?>();
		var call = 0;
		var handler = new StubHttpMessageHandler(request =>
		{
			tokens.Add(request.Headers.Authorization?.Parameter);
			requestIds.Add(request.Headers.GetValues("X-Request-ID").Single());
			call++;

			return call == 1
				? JsonResponse("""
					{
					  "result": false,
					  "data": { "message": "Unauthorized" },
					  "statusCode": "401"
					}
					""", HttpStatusCode.Unauthorized)
				: JsonResponse("""
					{
					  "result": true,
					  "data": { "artistId": 1, "name": "DeadSquad" },
					  "statusCode": "200"
					}
					""");
		});
		using var client = new OpxApiClient(
			"https://chinook.local",
			handler: handler,
			tokenProvider: tokenProvider,
			options: new OpxApiClientOptions { RequestIdFactory = () => "retry-request-1" });

		var result = await client.GetAsync<ArtistDto>("/api/artists/1");

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(call, Is.EqualTo(2));
			Assert.That(tokens, Is.EqualTo(new[] { "old-token", "retry-token" }));
			Assert.That(requestIds, Is.EqualTo(new[] { "retry-request-1", "retry-request-1" }));
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

	[Test]
	public async Task DownloadAsync_WhenResponseIsBinary_StreamsToDestinationAndReportsProgress()
	{
		var payload = Encoding.UTF8.GetBytes("zip-content");
		var progressValues = new List<OpxDownloadProgress>();
		var handler = new StubHttpMessageHandler(_ => BinaryResponse(payload));
		using var client = new OpxApiClient("https://chinook.local", handler: handler);
		await using var destination = new MemoryStream();

		var result = await client.DownloadAsync(
			"/api/files/{id}",
			destination,
			new OpxApiRequest
			{
				FromRoute = new { id = 10 },
				FromQuery = new { version = "1.0.0" },
				BearerToken = "download-token",
				Headers = new Dictionary<string, string?> { ["X-Download"] = "zip" }
			},
			new Progress<OpxDownloadProgress>(progressValues.Add));

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.BytesRead, Is.EqualTo(payload.Length));
			Assert.That(result.TotalBytes, Is.EqualTo(payload.Length));
			Assert.That(destination.ToArray(), Is.EqualTo(payload));
			Assert.That(progressValues.Last().BytesRead, Is.EqualTo(payload.Length));
			Assert.That(progressValues.Last().Percent, Is.EqualTo(100d).Within(0.01d));
			Assert.That(handler.LastRequest?.RequestUri?.ToString(), Is.EqualTo("https://chinook.local/api/files/10?version=1.0.0"));
			Assert.That(handler.LastRequest?.Headers.Accept.Single().MediaType, Is.EqualTo("*/*"));
			Assert.That(handler.LastRequest?.Headers.Authorization?.Parameter, Is.EqualTo("download-token"));
			Assert.That(handler.LastRequest?.Headers.GetValues("X-Download").Single(), Is.EqualTo("zip"));
		});
	}

	[Test]
	public async Task DownloadAsync_WhenUnauthorized_RefreshesAndRetriesOnce()
	{
		var tokenProvider = new OpxInMemoryTokenProvider
		{
			RefreshAsync = (_, _) => Task.FromResult<OpxTokenState?>(new OpxTokenState
			{
				AccessToken = "retry-download-token",
				RefreshToken = "refresh-token",
				ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
			})
		};
		await tokenProvider.SetTokenAsync("old-download-token", "refresh-token", DateTimeOffset.UtcNow.AddMinutes(10));
		var tokens = new List<string?>();
		var call = 0;
		var payload = Encoding.UTF8.GetBytes("zip-content");
		var handler = new StubHttpMessageHandler(request =>
		{
			tokens.Add(request.Headers.Authorization?.Parameter);
			call++;
			return call == 1
				? new HttpResponseMessage(HttpStatusCode.Unauthorized) { ReasonPhrase = "Unauthorized" }
				: BinaryResponse(payload);
		});
		using var client = new OpxApiClient("https://chinook.local", handler: handler, tokenProvider: tokenProvider);
		await using var destination = new MemoryStream();

		var result = await client.DownloadAsync("/api/files/update.zip", destination);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(call, Is.EqualTo(2));
			Assert.That(tokens, Is.EqualTo(new[] { "old-download-token", "retry-download-token" }));
			Assert.That(destination.ToArray(), Is.EqualTo(payload));
		});
	}

	[Test]
	public async Task PostDownloadAsync_WhenMultipartResponseIsBinary_StreamsBothDirections()
	{
		var input = Encoding.UTF8.GetBytes("document-content");
		var output = Encoding.UTF8.GetBytes("converted-content");
		var handler = new StubHttpMessageHandler(_ => BinaryResponse(output));
		using var client = new OpxApiClient("https://documents.local", handler: handler);
		await using var destination = new MemoryStream();

		var result = await client.PostDownloadAsync(
			"api/v1/document-conversions",
			destination,
			new OpxApiRequest
			{
				BearerToken = "document-token",
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
							OpenReadStream = () => new MemoryStream(input, writable: false)
						}
					]
				}
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(destination.ToArray(), Is.EqualTo(output));
			Assert.That(handler.LastRequest?.Method, Is.EqualTo(HttpMethod.Post));
			Assert.That(handler.LastRequest?.RequestUri?.ToString(), Is.EqualTo("https://documents.local/api/v1/document-conversions"));
			Assert.That(handler.LastRequest?.Content?.Headers.ContentType?.MediaType, Is.EqualTo("multipart/form-data"));
			Assert.That(handler.LastRequest?.Headers.Accept.Single().MediaType, Is.EqualTo("*/*"));
			Assert.That(handler.LastRequest?.Headers.Authorization?.Parameter, Is.EqualTo("document-token"));
			Assert.That(handler.LastRequestBody, Does.Contain("name=targetFormat"));
			Assert.That(handler.LastRequestBody, Does.Contain("Pdf"));
			Assert.That(handler.LastRequestBody, Does.Contain("filename=report.docx"));
			Assert.That(handler.LastRequestBody, Does.Contain("document-content"));
		});
	}

	private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		return new HttpResponseMessage(statusCode)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		};
	}

	private static HttpResponseMessage BinaryResponse(byte[] payload)
	{
		return new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new ByteArrayContent(payload)
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
