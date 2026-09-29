// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;

namespace RainPointClient;

/// <summary>A home configuration revision was announced over MQTT. Read current configuration to obtain names, plans and settings; this is not a valve-status report.</summary>
public sealed class RainPointConfigurationChange : EventArgs
	{
	/// <summary>
	/// Initializes configuration change from the supplied typed values.
	/// </summary>
	/// <param name="homeId">The positive cloud home identifier.</param>
	/// <param name="revision">The monotonically increasing accepted-observation or configuration revision.</param>
	internal RainPointConfigurationChange (long homeId, long revision)
		{
		HomeId = homeId;
		Revision = revision;
		}
	/// <summary>
	/// Gets the cloud home identifier associated with this record.
	/// </summary>
	public long HomeId
		{
		get;
		}
	/// <summary>
	/// Gets the configuration revision used to suppress repeated or older notifications.
	/// </summary>
	public long Revision
		{
		get;
		}
	}