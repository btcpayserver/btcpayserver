using BTCPayServer.Authentication;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer
{
    public static class Extensions
    {
		public static string WithTrailingSlash(this string str)
		{
			if(str.EndsWith("/"))
				return str;
			return str + "/";
		}

		public static string GetAbsoluteRoot(this HttpRequest request)
		{
			return string.Concat(
						request.Scheme,
						"://",
						request.Host.ToUriComponent(),
						request.PathBase.ToUriComponent());
		}

		public static IServiceCollection ConfigureBTCPayServer(this IServiceCollection services, IConfiguration conf)
		{
			services.Configure<BTCPayServerOptions>(o =>
			{
				o.LoadArgs(conf);
			});
			return services;
		}


		public static BitIdentity GetBitIdentity(this Controller controller, bool throws = true)
		{
			if(!(controller.User.Identity is BitIdentity))
				return throws ? throw new UnauthorizedAccessException("no-bitid") : (BitIdentity)null;
			return (BitIdentity)controller.User.Identity;
		}
	}
}
