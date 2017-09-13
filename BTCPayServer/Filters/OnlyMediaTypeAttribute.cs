using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.Extensions.Primitives;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Filters
{
    public class MediaTypeConstraintAttribute : Attribute,  IActionConstraint
	{
		public MediaTypeConstraintAttribute(string mediaType)
		{
			MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
		}

		public string MediaType
		{
			get; set;
		}

		public int Order => 100;

		public bool Accept(ActionConstraintContext context)
		{
			var match = context.RouteContext.HttpContext.Request.ContentType?.StartsWith(MediaType, StringComparison.Ordinal);
			return match.HasValue && match.Value;
		}
	}

	public class BitpayAPIConstraintAttribute : Attribute, IActionConstraint
	{
		public BitpayAPIConstraintAttribute(bool isBitpayAPI = true)
		{
			IsBitpayAPI = isBitpayAPI;
		}

		public bool IsBitpayAPI
		{
			get; set;
		}
		public int Order => 100;

		public bool Accept(ActionConstraintContext context)
		{
			return context.RouteContext.HttpContext.Request.Headers["x-accept-version"].Where(h => h == "2.0.0").Any() == IsBitpayAPI;
		}
	}

	public class AcceptMediaTypeConstraintAttribute : Attribute, IActionConstraint
	{
		public AcceptMediaTypeConstraintAttribute(string mediaType, bool expectedValue = true)
		{
			MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
			ExpectedValue = expectedValue;
		}

		public bool ExpectedValue
		{
			get; set;
		}

		public string MediaType
		{
			get; set;
		}

		public int Order => 100;

		public bool Accept(ActionConstraintContext context)
		{
			var match = context.RouteContext.HttpContext.Request.Headers["Accept"].FirstOrDefault()?.StartsWith(MediaType, StringComparison.Ordinal);
			return (match.HasValue && match.Value) == ExpectedValue;
		}
	}
}
