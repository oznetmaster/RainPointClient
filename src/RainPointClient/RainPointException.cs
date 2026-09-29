// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Net;

namespace RainPointClient;

/// <summary>A sanitized protocol error. Response bodies and credentials are not retained.</summary>
public sealed class RainPointException : Exception
	{
	/// <summary>
	/// Creates a cloud or protocol failure with optional service, HTTP and retry metadata.
	/// </summary>
	/// <param name="message">A diagnostic description that must not include credentials or private protocol payloads.</param>
	/// <param name="apiCode">The service result code, or null when no code was available.</param>
	/// <param name="httpStatus">The HTTP response status, or null when the failure had no HTTP status.</param>
	/// <param name="retryAfter">An optional service-suggested delay; it does not authorize automatic replay of a failed command.</param>
	internal RainPointException (string message, int? apiCode = null, HttpStatusCode? httpStatus = null,
		 TimeSpan? retryAfter = null) : base (message)
		{
		ApiCode = apiCode;
		HttpStatus = httpStatus;
		RetryAfter = retryAfter;
		}

	/// <summary>
	/// Gets the vendor result code, or null when no service code was available.
	/// </summary>
	public int? ApiCode
		{
		get;
		}
	/// <summary>
	/// Gets the HTTP failure status, or null for failures without an HTTP status.
	/// </summary>
	public HttpStatusCode? HttpStatus
		{
		get;
		}
	/// <summary>
	/// Gets a reported retry delay, or null when absent; the client does not replay a failed operation automatically.
	/// </summary>
	public TimeSpan? RetryAfter
		{
		get;
		}
	}