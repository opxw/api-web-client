// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public sealed class OpxDownloadResult
{
	public bool Result { get; init; }
	public string StatusCode { get; init; } = "0";
	public string? Message { get; init; }
	public long BytesRead { get; init; }
	public long? TotalBytes { get; init; }
	public bool IsSuccess => Result && StatusCode == "200";

	public static OpxDownloadResult Success(long bytesRead, long? totalBytes, string statusCode)
	{
		return new OpxDownloadResult
		{
			Result = true,
			StatusCode = statusCode,
			BytesRead = bytesRead,
			TotalBytes = totalBytes
		};
	}

	public static OpxDownloadResult Fail(string? message, string statusCode = "0", long bytesRead = 0, long? totalBytes = null)
	{
		return new OpxDownloadResult
		{
			Result = false,
			StatusCode = statusCode,
			Message = message,
			BytesRead = bytesRead,
			TotalBytes = totalBytes
		};
	}
}
