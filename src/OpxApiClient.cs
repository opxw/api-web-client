// Copyright (c) 2026 - opx
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;

namespace Opx.Api.Client;

public sealed class OpxApiClient : IOpxApiClient, IDisposable
{
	private readonly HttpClient _httpClient;
	private readonly bool _disposeHttpClient;
	private readonly JsonSerializerOptions _jsonOptions;
	private readonly Version _httpVersion;
	private readonly HttpVersionPolicy _httpVersionPolicy;
	private readonly OpxApiClientOptions _options;
	private readonly IOpxTokenProvider? _tokenProvider;
	private string _baseAddress;
	private string _errorMessage = string.Empty;
	private string _requestedUrl = string.Empty;
	private HttpResponseMessage? _responseMessage;

	public OpxApiClient(
		string baseAddress,
		Version? httpVersion = null,
		HttpVersionPolicy httpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
		HttpMessageHandler? handler = null,
		IOpxTokenProvider? tokenProvider = null,
		OpxApiClientOptions? options = null)
	{
		_options = options ?? new OpxApiClientOptions();
		_tokenProvider = tokenProvider;
		_httpVersion = httpVersion ?? System.Net.HttpVersion.Version20;
		_httpVersionPolicy = httpVersionPolicy;
		_httpClient = handler is null
			? new HttpClient(new SocketsHttpHandler { UseProxy = false })
			: new HttpClient(handler);
		_httpClient.Timeout = Timeout.InfiniteTimeSpan;
		_disposeHttpClient = true;
		_baseAddress = NormalizeBaseAddress(baseAddress);
		_jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};
	}

	public OpxApiClient(HttpClient httpClient, OpxApiClientOptions? options = null, IOpxTokenProvider? tokenProvider = null)
	{
		_options = options ?? new OpxApiClientOptions();
		_tokenProvider = tokenProvider;
		_httpClient = httpClient;
		_disposeHttpClient = false;
		_httpVersion = _options.HttpVersion;
		_httpVersionPolicy = _options.HttpVersionPolicy;
		_baseAddress = NormalizeBaseAddress(_options.BaseAddress ?? httpClient.BaseAddress?.ToString() ?? string.Empty);
		_jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};
	}

	public string BaseAddress
	{
		get => _baseAddress;
		set => _baseAddress = NormalizeBaseAddress(value);
	}

	public string RequestedUrl => _requestedUrl;
	public string ErrorMessage => _errorMessage;
	public HttpResponseMessage? ResponseMessage => _responseMessage;
	public TimeSpan TimeOut
	{
		get => _httpClient.Timeout;
		set => _httpClient.Timeout = value;
	}

	public Task<OpxApiResult<T>> GetAsync<T>(string path, OpxApiRequest? request = null)
	{
		return GetAsync<T>(path, request, CancellationToken.None);
	}

	public Task<OpxApiResult<T>> GetAsync<T>(string path, OpxApiRequest? request, CancellationToken cancellationToken)
	{
		return SendAsync<T>(HttpMethod.Get, path, request, cancellationToken);
	}

	public OpxApiResult<T> Get<T>(string path, OpxApiRequest? request = null)
	{
		return Send<T>(HttpMethod.Get, path, request);
	}

	public Task<OpxApiResult<T>> PostAsync<T>(string path, OpxApiRequest? request = null)
	{
		return PostAsync<T>(path, request, CancellationToken.None);
	}

	public Task<OpxApiResult<T>> PostAsync<T>(string path, OpxApiRequest? request, CancellationToken cancellationToken)
	{
		return SendAsync<T>(HttpMethod.Post, path, request, cancellationToken);
	}

	public OpxApiResult<T> Post<T>(string path, OpxApiRequest? request = null)
	{
		return Send<T>(HttpMethod.Post, path, request);
	}

	public Task<OpxApiResult<T>> PutAsync<T>(string path, OpxApiRequest? request = null)
	{
		return PutAsync<T>(path, request, CancellationToken.None);
	}

	public Task<OpxApiResult<T>> PutAsync<T>(string path, OpxApiRequest? request, CancellationToken cancellationToken)
	{
		return SendAsync<T>(HttpMethod.Put, path, request, cancellationToken);
	}

	public OpxApiResult<T> Put<T>(string path, OpxApiRequest? request = null)
	{
		return Send<T>(HttpMethod.Put, path, request);
	}

	public Task<OpxApiResult<T>> DeleteAsync<T>(string path, OpxApiRequest? request = null)
	{
		return DeleteAsync<T>(path, request, CancellationToken.None);
	}

	public Task<OpxApiResult<T>> DeleteAsync<T>(string path, OpxApiRequest? request, CancellationToken cancellationToken)
	{
		return SendAsync<T>(HttpMethod.Delete, path, request, cancellationToken);
	}

	public OpxApiResult<T> Delete<T>(string path, OpxApiRequest? request = null)
	{
		return Send<T>(HttpMethod.Delete, path, request);
	}

	public async Task<OpxApiResult<T>> SendAsync<T>(HttpMethod method, string path, OpxApiRequest? request = null)
	{
		return await SendAsync<T>(method, path, request, CancellationToken.None);
	}

	public async Task<OpxApiResult<T>> SendAsync<T>(HttpMethod method, string path, OpxApiRequest? request, CancellationToken cancellationToken)
	{
		var manualBearerToken = !string.IsNullOrWhiteSpace(request?.BearerToken);
		var bearerToken = await GetBearerTokenAsync(request, cancellationToken);
		var requestId = ResolveRequestId(request);
		using var message = CreateRequest(method, path, request, bearerToken, requestId);
		var result = await SendCoreAsync<T>(message, cancellationToken);

		if (!_options.RetryOnceOnUnauthorized || manualBearerToken || result.StatusCode != "401" || _tokenProvider is null)
		{
			return result;
		}

		var refreshedToken = await _tokenProvider.RefreshTokenAsync(cancellationToken);
		if (refreshedToken is not { HasAccessToken: true })
		{
			return result;
		}

		using var retryMessage = CreateRequest(method, path, request, refreshedToken.AccessToken, requestId);
		return await SendCoreAsync<T>(retryMessage, cancellationToken);
	}

	public OpxApiResult<T> Send<T>(HttpMethod method, string path, OpxApiRequest? request = null)
	{
		return SendAsync<T>(method, path, request).GetAwaiter().GetResult();
	}

	public async Task<OpxDownloadResult> DownloadAsync(
		string path,
		Stream destination,
		OpxApiRequest? request = null,
		IProgress<OpxDownloadProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);

		if (!destination.CanWrite)
		{
			throw new ArgumentException("Destination stream must be writable.", nameof(destination));
		}

		var manualBearerToken = !string.IsNullOrWhiteSpace(request?.BearerToken);
		var bearerToken = await GetBearerTokenAsync(request, cancellationToken);
		var requestId = ResolveRequestId(request);
		using var message = CreateRequest(HttpMethod.Get, path, request, bearerToken, requestId, acceptJson: false);
		var result = await DownloadCoreAsync(message, destination, progress, cancellationToken);

		if (!_options.RetryOnceOnUnauthorized || manualBearerToken || result.StatusCode != "401" || _tokenProvider is null)
		{
			return result;
		}

		var refreshedToken = await _tokenProvider.RefreshTokenAsync(cancellationToken);
		if (refreshedToken is not { HasAccessToken: true })
		{
			return result;
		}

		using var retryMessage = CreateRequest(
			HttpMethod.Get,
			path,
			request,
			refreshedToken.AccessToken,
			requestId,
			acceptJson: false);
		return await DownloadCoreAsync(retryMessage, destination, progress, cancellationToken);
	}

	public async Task<OpxDownloadResult> PostDownloadAsync(
		string path,
		Stream destination,
		OpxApiRequest request,
		IProgress<OpxDownloadProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);
		ArgumentNullException.ThrowIfNull(request);

		if (!destination.CanWrite)
		{
			throw new ArgumentException("Destination stream must be writable.", nameof(destination));
		}

		if (request.FromMultipart is null)
		{
			throw new ArgumentException("A multipart form is required.", nameof(request));
		}

		var manualBearerToken = !string.IsNullOrWhiteSpace(request.BearerToken);
		var bearerToken = await GetBearerTokenAsync(request, cancellationToken);
		var requestId = ResolveRequestId(request);
		using var message = CreateRequest(HttpMethod.Post, path, request, bearerToken, requestId, acceptJson: false);
		var result = await DownloadCoreAsync(message, destination, progress, cancellationToken);

		if (!_options.RetryOnceOnUnauthorized || manualBearerToken || result.StatusCode != "401" || _tokenProvider is null)
		{
			return result;
		}

		var refreshedToken = await _tokenProvider.RefreshTokenAsync(cancellationToken);
		if (refreshedToken is not { HasAccessToken: true })
		{
			return result;
		}

		using var retryMessage = CreateRequest(
			HttpMethod.Post,
			path,
			request,
			refreshedToken.AccessToken,
			requestId,
			acceptJson: false);
		return await DownloadCoreAsync(retryMessage, destination, progress, cancellationToken);
	}

	public void Dispose()
	{
		_responseMessage?.Dispose();
		if (_disposeHttpClient)
		{
			_httpClient.Dispose();
		}
	}

	private async Task<OpxDownloadResult> DownloadCoreAsync(
		HttpRequestMessage message,
		Stream destination,
		IProgress<OpxDownloadProgress>? progress,
		CancellationToken cancellationToken)
	{
		HttpResponseMessage? responseMessage = null;
		try
		{
			responseMessage = await _httpClient.SendAsync(
				message,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var statusCode = GetStatusCode(responseMessage);
			var totalBytes = responseMessage.Content.Headers.ContentLength;

			if (!responseMessage.IsSuccessStatusCode)
			{
				_errorMessage = !string.IsNullOrWhiteSpace(responseMessage.ReasonPhrase)
					? responseMessage.ReasonPhrase!
					: responseMessage.ToString();
				return OpxDownloadResult.Fail(_errorMessage, statusCode, totalBytes: totalBytes);
			}

			await using var source = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
			var bytesRead = await CopyToAsync(source, destination, totalBytes, progress, cancellationToken);
			return OpxDownloadResult.Success(bytesRead, totalBytes, statusCode);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_errorMessage = ex.Message;
			return OpxDownloadResult.Fail(_errorMessage, GetStatusCode(responseMessage));
		}
		finally
		{
			if (responseMessage is not null)
			{
				var previousResponse = Interlocked.Exchange(ref _responseMessage, responseMessage);
				previousResponse?.Dispose();
			}
		}
	}

	private static async Task<long> CopyToAsync(
		Stream source,
		Stream destination,
		long? totalBytes,
		IProgress<OpxDownloadProgress>? progress,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[81920];
		long bytesRead = 0;
		int read;

		while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
		{
			await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			bytesRead += read;
			progress?.Report(new OpxDownloadProgress(bytesRead, totalBytes));
		}

		return bytesRead;
	}

	private async Task<OpxApiResult<T>> SendCoreAsync<T>(HttpRequestMessage message, CancellationToken cancellationToken)
	{
		HttpResponseMessage? responseMessage = null;
		try
		{
			responseMessage = await _httpClient.SendAsync(message, cancellationToken);
			var httpStatusCode = GetStatusCode(responseMessage);

			if (!responseMessage.IsSuccessStatusCode && !_options.ParseErrorResponseBody)
			{
				return CreateHttpErrorResult<T>(responseMessage, httpStatusCode);
			}

			try
			{
				await using var stream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
				using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
				return ReadResponse<T>(document.RootElement, httpStatusCode);
			}
			catch (JsonException) when (!responseMessage.IsSuccessStatusCode)
			{
				return CreateHttpErrorResult<T>(responseMessage, httpStatusCode);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_errorMessage = ex.Message;
			return OpxApiResult<T>.Fail(_errorMessage, GetStatusCode(responseMessage));
		}
		finally
		{
			if (responseMessage is not null)
			{
				var previousResponse = Interlocked.Exchange(ref _responseMessage, responseMessage);
				previousResponse?.Dispose();
			}
		}
	}

	private OpxApiResult<T> CreateHttpErrorResult<T>(HttpResponseMessage responseMessage, string statusCode)
	{
		_errorMessage = !string.IsNullOrWhiteSpace(responseMessage.ReasonPhrase)
			? responseMessage.ReasonPhrase!
			: responseMessage.ToString();
		return OpxApiResult<T>.Fail(_errorMessage, statusCode);
	}

	private static string GetStatusCode(HttpResponseMessage? responseMessage)
	{
		return ((int?)responseMessage?.StatusCode)?.ToString() ?? "0";
	}

	private OpxApiResult<T> ReadResponse<T>(JsonElement root, string httpStatusCode)
	{
		if (root.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
		{
			return OpxApiResult<T>.Fail("Empty response", httpStatusCode);
		}

		var result = root.TryGetProperty("result", out var resultElement)
			&& resultElement.ValueKind is JsonValueKind.True or JsonValueKind.False
			&& resultElement.GetBoolean();
		var statusCode = root.TryGetProperty("statusCode", out var statusCodeElement)
			? statusCodeElement.ToString()
			: httpStatusCode;
		var data = root.TryGetProperty("data", out var dataElement)
			? dataElement
			: default;

		if (!result)
		{
			return new OpxApiResult<T>
			{
				Result = false,
				StatusCode = string.IsNullOrWhiteSpace(statusCode) ? httpStatusCode : statusCode,
				Message = TryReadMessage(data)
			};
		}

		return OpxApiResult<T>.FromTypedResponse(new OpxApiResponse<T>
		{
			Result = true,
			StatusCode = string.IsNullOrWhiteSpace(statusCode) ? httpStatusCode : statusCode,
			Data = ConvertData<T>(data)
		});
	}

	private T? ConvertData<T>(JsonElement data)
	{
		if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
		{
			return default;
		}

		if (typeof(T) == typeof(JsonElement))
		{
			return (T)(object)data.Clone();
		}

		if (typeof(T) == typeof(string))
		{
			return (T)(object)data.ToString();
		}

		return data.Deserialize<T>(_jsonOptions);
	}

	private static string? TryReadMessage(JsonElement data)
	{
		if (data.ValueKind == JsonValueKind.String)
		{
			return data.GetString();
		}

		if (data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty("message", out var message)
			&& message.ValueKind == JsonValueKind.String)
		{
			return message.GetString();
		}

		return null;
	}

	private async Task<string?> GetBearerTokenAsync(OpxApiRequest? request, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(request?.BearerToken))
		{
			return request.BearerToken;
		}

		if (_tokenProvider is null)
		{
			return null;
		}

		var token = await _tokenProvider.GetTokenAsync(cancellationToken);
		if (token is null)
		{
			return null;
		}

		var utcNow = DateTimeOffset.UtcNow;
		if (token.IsExpired(utcNow) || token.ShouldRefresh(utcNow, _options.RefreshTokenBeforeExpires))
		{
			token = await _tokenProvider.RefreshTokenAsync(cancellationToken);
		}

		return token is { HasAccessToken: true } && !token.IsExpired(DateTimeOffset.UtcNow)
			? token.AccessToken
			: null;
	}

	private HttpRequestMessage CreateRequest(
		HttpMethod method,
		string path,
		OpxApiRequest? request,
		string? bearerToken,
		string? requestId,
		bool acceptJson = true)
	{
		var url = BuildUrl(path, request);
		_requestedUrl = url;
		var message = new HttpRequestMessage(method, url)
		{
			Version = _httpVersion,
			VersionPolicy = _httpVersionPolicy
		};

		message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptJson ? "application/json" : "*/*"));
		if (!string.IsNullOrWhiteSpace(bearerToken))
		{
			message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		}

		if (request?.Headers is not null)
		{
			foreach (var header in request.Headers)
			{
				if (!string.IsNullOrWhiteSpace(header.Key) && header.Value is not null)
				{
					message.Headers.TryAddWithoutValidation(header.Key, header.Value);
				}
			}
		}

		if (!string.IsNullOrWhiteSpace(requestId)
			&& !string.IsNullOrWhiteSpace(_options.RequestIdHeaderName)
			&& !message.Headers.Contains(_options.RequestIdHeaderName))
		{
			message.Headers.TryAddWithoutValidation(_options.RequestIdHeaderName, requestId);
		}

		if (request?.FromBody is not null && request.FromMultipart is not null)
		{
			throw new InvalidOperationException("A request cannot contain both JSON body and multipart form data.");
		}

		if (request?.FromMultipart is not null)
		{
			message.Content = CreateMultipartContent(request.FromMultipart);
		}
		else if (request?.FromBody is not null)
		{
			var json = JsonSerializer.Serialize(request.FromBody, _jsonOptions);
			message.Content = new StringContent(json, Encoding.UTF8, "application/json");
		}
		else if (method != HttpMethod.Get && method != HttpMethod.Delete)
		{
			message.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
		}

		return message;
	}

	private static MultipartFormDataContent CreateMultipartContent(OpxMultipartFormData form)
	{
		var content = new MultipartFormDataContent();

		try
		{
			foreach (var field in form.Fields)
			{
				if (string.IsNullOrWhiteSpace(field.Key))
				{
					throw new ArgumentException("Multipart field names cannot be empty.", nameof(form));
				}

				content.Add(new StringContent(field.Value ?? string.Empty, Encoding.UTF8), field.Key);
			}

			foreach (var file in form.Files)
			{
				if (string.IsNullOrWhiteSpace(file.Name) || string.IsNullOrWhiteSpace(file.FileName))
				{
					throw new ArgumentException("Multipart file name and form field name are required.", nameof(form));
				}

				var stream = file.OpenReadStream()
					?? throw new InvalidOperationException("Multipart file stream factory returned null.");

				if (!stream.CanRead)
				{
					stream.Dispose();
					throw new ArgumentException("Multipart file stream must be readable.", nameof(form));
				}

				var fileContent = new StreamContent(stream);
				if (!MediaTypeHeaderValue.TryParse(file.ContentType, out var contentType))
				{
					fileContent.Dispose();
					throw new ArgumentException("Multipart file content type is invalid.", nameof(form));
				}

				fileContent.Headers.ContentType = contentType;
				content.Add(fileContent, file.Name, Path.GetFileName(file.FileName));
			}

			return content;
		}
		catch
		{
			content.Dispose();
			throw;
		}
	}

	private string? ResolveRequestId(OpxApiRequest? request)
	{
		var requestId = request?.RequestId;
		if (string.IsNullOrWhiteSpace(requestId)
			&& request?.Headers is not null
			&& !string.IsNullOrWhiteSpace(_options.RequestIdHeaderName)
			&& request.Headers.TryGetValue(_options.RequestIdHeaderName, out var headerRequestId))
		{
			requestId = headerRequestId;
		}

		if (string.IsNullOrWhiteSpace(requestId) && _options.GenerateRequestId)
		{
			requestId = _options.RequestIdFactory();
		}

		if (string.IsNullOrWhiteSpace(requestId))
		{
			return null;
		}

		requestId = requestId.Trim();
		if (requestId.Length > 128)
		{
			throw new ArgumentException("Request ID cannot exceed 128 characters.", nameof(request));
		}

		return requestId;
	}

	private string BuildUrl(string path, OpxApiRequest? request)
	{
		if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
		{
			return ApplyQuery(absoluteUri.ToString(), request?.FromQuery);
		}

		var routePath = ApplyRoute(path, request?.FromRoute);
		var url = string.Concat(_baseAddress, routePath.TrimStart('/'));
		return ApplyQuery(url, request?.FromQuery);
	}

	private static string ApplyRoute(string path, object? route)
	{
		if (route is null)
		{
			return path;
		}

		var result = path;
		foreach (var property in route.GetType().GetProperties())
		{
			var value = Uri.EscapeDataString(property.GetValue(route)?.ToString() ?? string.Empty);
			result = result.Replace("{" + property.Name + "}", value, StringComparison.OrdinalIgnoreCase);
		}

		return result;
	}

	private static string ApplyQuery(string url, object? query)
	{
		if (query is null)
		{
			return url;
		}

		var values = query.GetType()
			.GetProperties()
			.Select(property => new
			{
				property.Name,
				Value = property.GetValue(query)
			})
			.Where(item => item.Value is not null)
			.Select(item => $"{Uri.EscapeDataString(item.Name)}={Uri.EscapeDataString(item.Value!.ToString() ?? string.Empty)}")
			.ToArray();

		if (values.Length == 0)
		{
			return url;
		}

		var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
		return string.Concat(url, separator, string.Join("&", values));
	}

	private static string NormalizeBaseAddress(string baseAddress)
	{
		if (string.IsNullOrWhiteSpace(baseAddress))
		{
			return string.Empty;
		}

		return baseAddress.EndsWith("/", StringComparison.Ordinal)
			? baseAddress
			: string.Concat(baseAddress, "/");
	}
}
