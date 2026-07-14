// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public sealed class OpxApiClientException : Exception
{
	public OpxApiClientException(string? message, string statusCode)
		: base(string.IsNullOrWhiteSpace(message) ? $"Opx API request failed with status code {statusCode}." : message)
	{
		StatusCode = statusCode;
	}

	public string StatusCode { get; }
}
