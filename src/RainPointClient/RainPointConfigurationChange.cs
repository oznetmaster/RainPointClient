// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;

namespace RainPointClient;

/// <summary>A home configuration revision was announced over MQTT. Read current configuration to obtain names, plans and settings; this is not a valve-status report.</summary>
public sealed class RainPointConfigurationChange : EventArgs
	{
	internal RainPointConfigurationChange (long homeId, long revision)
		{
		HomeId = homeId;
		Revision = revision;
		}
	public long HomeId
		{
		get;
		}
	public long Revision
		{
		get;
		}
	}