using BTCPayServer.Authentication;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer
{
    public static class Extensions
    {
		public static BitIdentity GetBitIdentity(this Controller controller)
		{
			if(!(controller.User.Identity is BitIdentity))
				throw new UnauthorizedAccessException("no-bitid");
			return (BitIdentity)controller.User.Identity;
		}
	}
}
