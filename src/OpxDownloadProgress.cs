// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public readonly record struct OpxDownloadProgress(
	long BytesRead,
	long? TotalBytes)
{
	public double? Percent => TotalBytes is > 0
		? BytesRead * 100d / TotalBytes.Value
		: null;
}
