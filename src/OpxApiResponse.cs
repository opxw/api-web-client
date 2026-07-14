// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public sealed class OpxApiResponse<T>
{
	public bool Result { get; set; }
	public T? Data { get; set; }
	public string StatusCode { get; set; } = "0";
}
