// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public sealed class OpxApiClientOptions
{
	public string BaseAddress { get; set; } = string.Empty;
	public Version HttpVersion { get; set; } = System.Net.HttpVersion.Version20;
	public HttpVersionPolicy HttpVersionPolicy { get; set; } = HttpVersionPolicy.RequestVersionOrLower;
	public TimeSpan Timeout { get; set; } = System.Threading.Timeout.InfiniteTimeSpan;
	public TimeSpan RefreshTokenBeforeExpires { get; set; } = TimeSpan.FromSeconds(60);
	public bool RetryOnceOnUnauthorized { get; set; } = true;
	public bool ParseErrorResponseBody { get; set; } = true;
	public OpxApiResponseMode ResponseMode { get; set; } = OpxApiResponseMode.OpxEnvelope;
	public bool EnableExecutedEndpointLogging { get; set; } = false;
	public bool GenerateRequestId { get; set; } = true;
	public string RequestIdHeaderName { get; set; } = "X-Request-ID";
	public Func<string> RequestIdFactory { get; set; } = static () => Guid.NewGuid().ToString("N");
}
