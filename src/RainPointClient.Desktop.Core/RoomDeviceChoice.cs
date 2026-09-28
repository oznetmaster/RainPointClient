// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace RainPointClient.Desktop.Core;

public sealed class RoomDeviceChoice
	{
	internal RoomDeviceChoice (RainPointRoomDevice assignment, string label, bool selected)
		{
		Assignment = assignment;
		Label = label;
		IsSelected = selected;
		}
	internal RainPointRoomDevice Assignment
		{
		get;
		}
	public string Label
		{
		get;
		}
	public bool IsSelected
		{
		get; set;
		}
	}