using BTCPayServer.Authentication;
using BTCPayServer.Configuration;
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

		public static IServiceCollection ConfigureBTCPayServer(this IServiceCollection services, IConfiguration conf)
		{
			services.Configure<BTCPayServerOptions>(o =>
			{
				o.LoadArgs(conf);
			});
			return services;
		}


		public static BitIdentity GetBitIdentity(this Controller controller)
		{
			if(!(controller.User.Identity is BitIdentity))
				throw new UnauthorizedAccessException("no-bitid");
			return (BitIdentity)controller.User.Identity;
		}
	}
}
