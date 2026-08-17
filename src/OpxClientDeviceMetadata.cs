// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public static class OpxClientDeviceTypes
{
	public const string Desktop = "desktop";
	public const string Mobile = "mobile";
	public const string Web = "web";
}

public sealed class OpxClientDeviceMetadata
{
	public required string DeviceId { get; init; }
	public required string DeviceType { get; init; }
	public string? DeviceName { get; init; }
	public string? Platform { get; init; }
	public string? ClientApplication { get; init; }
	public string? ClientVersion { get; init; }

	public static OpxClientDeviceMetadata CreateDesktop(
		string deviceId,
		string clientApplication,
		string? clientVersion = null,
		string? deviceName = null,
		string? platform = null)
	{
		return new OpxClientDeviceMetadata
		{
			DeviceId = deviceId,
			DeviceType = OpxClientDeviceTypes.Desktop,
			DeviceName = deviceName ?? Environment.MachineName,
			Platform = platform ?? Environment.OSVersion.Platform.ToString(),
			ClientApplication = clientApplication,
			ClientVersion = clientVersion
		};
	}

	public static OpxClientDeviceMetadata CreateMobile(
		string deviceId,
		string clientApplication,
		string? clientVersion = null,
		string? deviceName = null,
		string? platform = null)
	{
		return new OpxClientDeviceMetadata
		{
			DeviceId = deviceId,
			DeviceType = OpxClientDeviceTypes.Mobile,
			DeviceName = deviceName,
			Platform = platform,
			ClientApplication = clientApplication,
			ClientVersion = clientVersion
		};
	}

	public static OpxClientDeviceMetadata CreateWeb(
		string browserSessionId,
		string clientApplication,
		string? clientVersion = null,
		string? platform = null)
	{
		return new OpxClientDeviceMetadata
		{
			DeviceId = browserSessionId,
			DeviceType = OpxClientDeviceTypes.Web,
			Platform = platform,
			ClientApplication = clientApplication,
			ClientVersion = clientVersion
		};
	}
}
