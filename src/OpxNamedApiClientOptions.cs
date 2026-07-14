// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

internal sealed class OpxNamedApiClientOptions
{
	public OpxNamedApiClientOptions(string name, OpxApiClientOptions options)
	{
		Name = name;
		Options = options;
	}

	public string Name { get; }
	public OpxApiClientOptions Options { get; }
}
