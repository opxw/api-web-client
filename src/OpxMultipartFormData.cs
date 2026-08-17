// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public sealed class OpxMultipartFormData
{
	public Dictionary<string, string?> Fields { get; init; } = new(StringComparer.Ordinal);
	public IReadOnlyCollection<OpxMultipartFile> Files { get; init; } = [];
}

public sealed class OpxMultipartFile
{
	public required string Name { get; init; }
	public required string FileName { get; init; }
	public string ContentType { get; init; } = "application/octet-stream";
	public required Func<Stream> OpenReadStream { get; init; }
}
