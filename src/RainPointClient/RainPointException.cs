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