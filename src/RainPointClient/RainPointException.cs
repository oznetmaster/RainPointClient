// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Net;

namespace RainPointClient;

/// <summary>A sanitized protocol error. Response bodies and credentials are not retained.</summary>
public sealed class RainPointException : Exception
	{
	internal RainPointException (string message, int? apiCode = null, HttpStatusCode? httpStatus = null,
		 TimeSpan? retryAfter = null) : base (message)
		{
		ApiCode = apiCode;
		HttpStatus = httpStatus;
		RetryAfter = retryAfter;
		}

	public int? ApiCode
		{
		get;
		}
	public HttpStatusCode? HttpStatus
		{
		get;
		}
	public TimeSpan? RetryAfter
		{
		get;
		}
	}