// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
namespace RainPointClient;
/// <summary>A room's hub, child device or individual child zone assignment.</summary>
public sealed class RainPointRoomDevice
	{
	public RainPointRoomDevice (long hubId, long? deviceId = null, int? zone = null)
		{
		if (hubId <= 0)
			throw new ArgumentOutOfRangeException (nameof (hubId));
		if (deviceId.HasValue && deviceId <= 0)
			throw new ArgumentOutOfRangeException (nameof (deviceId));
		if (zone.HasValue && (!deviceId.HasValue || zone is < 1 or > 3))
			throw new ArgumentOutOfRangeException (nameof (zone));
		HubId = hubId;
		DeviceId = deviceId;
		Zone = zone;
		}
	public long HubId
		{
		get;
		}
	public long? DeviceId
		{
		get;
		}
	public int? Zone
		{
		get;
		}
	internal string Encode () => HubId.ToString (CultureInfo.InvariantCulture) + (DeviceId.HasValue ? "#" + DeviceId.Value.ToString (CultureInfo.InvariantCulture) : "") + (Zone.HasValue ? "#" + Zone.Value.ToString (CultureInfo.InvariantCulture) : "");
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Resolves both current device-ID and legacy RF-address room assignments. Unsupported assignments fail explicitly.</summary>
	public async Task<IReadOnlyList<RainPointRoomDevice>> GetRoomDevicesAsync (RainPointHomeDetails home, long roomId, CancellationToken cancellationToken = default)
		{
		RequireRoom (home, roomId);
		var hubs = await GetHubsAsync (home.Id, cancellationToken).ConfigureAwait (false);
		string? encoded = home.Rooms.Single (r => r.Id == roomId).Wire.Devices;
		if (string.IsNullOrEmpty (encoded))
			return Array.Empty<RainPointRoomDevice> ();
		var result = new List<RainPointRoomDevice> ();
		foreach (string item in encoded!.Split (','))
			{
			bool legacy = item.IndexOf ('-') >= 0;
			string[] fields = item.Split (legacy ? '-' : '#');
			if (fields.Length is < 1 or > 3 || !long.TryParse (fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out long mid) || mid <= 0)
				throw new RainPointException ("Unsupported room device assignment.");
			RainPointHub? hub = hubs.SingleOrDefault (h => h.Id == mid);
			if (hub is null)
				throw new RainPointException ("Room references an unavailable hub.");
			long? sid = null;
			int? zone = null;
			if (fields.Length > 1)
				{
				if (!long.TryParse (fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long child) || child <= 0)
					throw new RainPointException ("Unsupported room child assignment.");
				var devices = hub.Devices.Where (d => legacy ? d.Address == child : d.Id == child).ToArray ();
				if (devices.Length != 1 || devices[0].Id is not > 0)
					throw new RainPointException ("Room child identity is unavailable or ambiguous.");
				sid = devices[0].Id;
				if (fields.Length == 3)
					{
					if (!int.TryParse (fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || !devices[0].SupportedZoneCount.HasValue || port > devices[0].SupportedZoneCount)
						throw new RainPointException ("Unsupported room zone assignment.");
					zone = port;
					}
				}
			result.Add (new (mid, sid, zone));
			}
		if (result.Select (r => r.Encode ()).Distinct ().Count () != result.Count)
			throw new RainPointException ("Duplicate room assignments.");
		return result.AsReadOnly ();
		}
	/// <summary>Replaces only the selected room's assignments using discovered devices, including zones 1–3.</summary>
	public async Task SetRoomDevicesAsync (RainPointHomeDetails expected, long roomId, IReadOnlyList<RainPointRoomDevice> assignments, CancellationToken cancellationToken = default)
		{
		RequireRoom (expected, roomId);
		if (assignments is null)
			throw new ArgumentNullException (nameof (assignments));
		var values = assignments.ToArray ();
		if (values.Any (v => v is null) || values.Select (v => v.Encode ()).Distinct ().Count () != values.Length)
			throw new ArgumentException ("Assignments must be present and distinct.", nameof (assignments));
		var hubs = await GetHubsAsync (expected.Id, cancellationToken).ConfigureAwait (false);
		foreach (var value in values)
			{
			var hub = hubs.SingleOrDefault (h => h.Id == value.HubId) ?? throw new ArgumentException ("Assignment hub is not in this home.", nameof (assignments));
			if (value.DeviceId.HasValue)
				{
				var children = hub.Devices.Where (d => d.Id == value.DeviceId).ToArray ();
				if (children.Length != 1 || (value.Zone.HasValue && (!children[0].SupportedZoneCount.HasValue || value.Zone > children[0].SupportedZoneCount)))
					throw new ArgumentException ("Assignment child or zone is unavailable.", nameof (assignments));
				}
			}
		await WriteHomeAsync (expected, "app/member/appHome/room/update", new RoomPatch { HomeId = expected.Id, Id = roomId, Devices = string.Join (",", values.Select (v => v.Encode ())) }, cancellationToken).ConfigureAwait (false);
		}
	public Task RenameHubAsync (RainPointHomeDetails home, RainPointHub expected, string name, CancellationToken cancellationToken = default) => RenameDeviceCoreAsync (home, expected, null, name, cancellationToken);
	public Task RenameDeviceAsync (RainPointHomeDetails home, RainPointHub hub, int address, string name, CancellationToken cancellationToken = default) => RenameDeviceCoreAsync (home, hub, address, name, cancellationToken);
	private async Task RenameDeviceCoreAsync (RainPointHomeDetails home, RainPointHub expected, int? address, string name, CancellationToken token)
		{
		if (home is null)
			throw new ArgumentNullException (nameof (home));
		ValidateHub (expected);
		RequireText (name, nameof (name));
		if (home.Id != expected.HomeId)
			throw new ArgumentException ("The hub belongs to a different home.", nameof (expected));
		var hubs = await GetHubsAsync (home.Id, token).ConfigureAwait (false);
		var current = hubs.SingleOrDefault (h => h.Id == expected.Id);
		if (current is null || current.DeviceName != expected.DeviceName || current.ProductKey != expected.ProductKey || current.Model != expected.Model || current.Name != expected.Name)
			throw new InvalidOperationException ("Hub identity or name changed. Reload before writing.");
		long? sid = null;
		if (address.HasValue)
			{
			var original = expected.Devices.SingleOrDefault (d => d.Address == address);
			var child = current.Devices.SingleOrDefault (d => d.Address == address);
			if (original?.Id is not > 0 || child is null || original.Id != child.Id || original.Model != child.Model || original.Name != child.Name)
				throw new InvalidOperationException ("Child identity or name changed. Reload before writing.");
			sid = original.Id;
			}
		await WriteHomeAsync (home, address.HasValue ? "app/device/sub/update" : "app/device/main/update", new RenameDeviceRequest { HubId = expected.Id, DeviceId = sid, Name = name }, token).ConfigureAwait (false);
		}
	private sealed class RenameDeviceRequest
		{
		[JsonPropertyName ("mid")]
		public long HubId
			{
			get; set;
			}
		[JsonPropertyName ("sid"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public long? DeviceId
			{
			get; set;
			}
		[JsonPropertyName ("name")] public string Name { get; set; } = string.Empty;
		}
	}