// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public interface IOpxApiClientFactory
{
	IOpxApiClient CreateClient(string name);
}
