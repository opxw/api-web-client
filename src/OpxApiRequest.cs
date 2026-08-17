// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public sealed class OpxApiRequest
{
	public object? FromRoute { get; set; }
	public object? FromQuery { get; set; }
	public object? FromBody { get; set; }
	public OpxMultipartFormData? FromMultipart { get; set; }
	public string? BearerToken { get; set; }
	public string? RequestId { get; set; }
	public Dictionary<string, string?>? Headers { get; set; }
}
