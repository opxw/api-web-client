// Copyright (c) 2026 - opx
using System.Text.Json;

namespace Opx.Api.Client;

public sealed class OpxApiResult<T>
{
	public bool Result { get; init; }
	public T? Data { get; init; }
	public string StatusCode { get; init; } = "0";
	public string? Message { get; init; }
	public bool IsSuccess => Result && StatusCode == "200";

	public OpxApiResult<T> EnsureSuccess()
	{
		if (!IsSuccess)
		{
			throw new OpxApiClientException(Message, StatusCode);
		}

		return this;
	}

	public T GetDataOrThrow()
	{
		EnsureSuccess();
		if (Data is null)
		{
			throw new OpxApiClientException("Response data is null.", StatusCode);
		}

		return Data;
	}

	public T? GetDataOrDefault(T? defaultValue = default)
	{
		return IsSuccess ? Data : defaultValue;
	}

	public static OpxApiResult<T> FromResponse(OpxApiResponse<JsonElement> response, JsonSerializerOptions options)
	{
		return new OpxApiResult<T>
		{
			Result = response.Result,
			Data = ConvertData<T>(response.Data, options),
			StatusCode = response.StatusCode,
			Message = TryReadMessage(response.Data)
		};
	}

	public static OpxApiResult<T> FromTypedResponse(OpxApiResponse<T> response, string? message = null)
	{
		return new OpxApiResult<T>
		{
			Result = response.Result,
			Data = response.Data,
			StatusCode = response.StatusCode,
			Message = message
		};
	}

	public static OpxApiResult<T> Fail(string? message, string statusCode = "0")
	{
		return new OpxApiResult<T>
		{
			Result = false,
			StatusCode = statusCode,
			Message = message
		};
	}

	private static TResult? ConvertData<TResult>(JsonElement data, JsonSerializerOptions options)
	{
		if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
		{
			return default;
		}

		if (typeof(TResult) == typeof(JsonElement))
		{
			return (TResult)(object)data;
		}

		if (typeof(TResult) == typeof(string))
		{
			return (TResult)(object)data.ToString();
		}

		return data.Deserialize<TResult>(options);
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
}
