using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http.Extensions;

namespace BTCPayServer
{
	public interface IExternalUrlProvider
	{
		string GetEncodedUrl();
		string GetAbsolute(string path);
	}

	public class DefaultExternalUrlProvider : IExternalUrlProvider
	{
		IHttpContextAccessor _ContextAccessor;
		public DefaultExternalUrlProvider(IHttpContextAccessor contextAccessor)
		{
			if(contextAccessor == null)
				throw new ArgumentNullException(nameof(contextAccessor));
			_ContextAccessor = contextAccessor;
		}
		public string GetAbsolute(string path)
		{
			var request = _ContextAccessor.HttpContext.Request;
			var builder = new UriBuilder()
			{
				Scheme = request.Scheme,
				Host = request.Host.Host,
			};
			if(request.Host.Port.HasValue)
				builder.Port = request.Host.Port.Value;
			return builder.Uri.AbsoluteUri + path;
		}

		public string GetEncodedUrl()
		{
			var request = _ContextAccessor.HttpContext.Request;
			return request.GetEncodedUrl();
		}
	}

	public class FixedExternalUrlProvider : IExternalUrlProvider
	{
		string _Url;
		IHttpContextAccessor _ContextAccessor;
		public FixedExternalUrlProvider(Uri url, IHttpContextAccessor contextAccessor)
		{
			if(url == null)
				throw new ArgumentNullException(nameof(url));
			if(contextAccessor == null)
				throw new ArgumentNullException(nameof(contextAccessor));
			_ContextAccessor = contextAccessor;
			_Url = url.AbsoluteUri;
		}

		public string GetAbsolute(string path)
		{
			var uri = new Uri(_Url, UriKind.Absolute);
			var builder = new UriBuilder()
			{
				Scheme = uri.Scheme,
				Host = uri.Host,
			};
			if(!uri.IsDefaultPort)
				builder.Port = uri.Port;
			return builder.Uri.AbsoluteUri + path;
		}

		public string GetEncodedUrl()
		{
			var req = _ContextAccessor.HttpContext.Request;
			return BuildAbsolute(req.Path, req.QueryString); ;
		}

		private string BuildAbsolute(PathString path = new PathString(),
			QueryString query = new QueryString(),
			FragmentString fragment = new FragmentString())
		{

			var combinedPath = path.HasValue ? path.Value.Substring(1) : "";

			var encodedQuery = query.ToString();
			var encodedFragment = fragment.ToString();

			// PERF: Calculate string length to allocate correct buffer size for StringBuilder.
			var length = _Url.Length + combinedPath.Length + encodedQuery.Length + encodedFragment.Length;

			return new StringBuilder(length)
				.Append(_Url)
				.Append(combinedPath)
				.Append(encodedQuery)
				.Append(encodedFragment)
				.ToString();
		}
	}
}
