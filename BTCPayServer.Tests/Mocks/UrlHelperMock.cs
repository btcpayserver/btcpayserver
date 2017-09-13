using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BTCPayServer.Tests.Mocks
{
	public class UrlHelperMock : IUrlHelper
	{
		public ActionContext ActionContext => throw new NotImplementedException();

		public string Action(UrlActionContext actionContext)
		{
			return "http://127.0.0.1/mock";
		}

		public string Content(string contentPath)
		{
			return "http://127.0.0.1/mock";
		}

		public bool IsLocalUrl(string url)
		{
			return false;
		}

		public string Link(string routeName, object values)
		{
			return "http://127.0.0.1/mock";
		}

		public string RouteUrl(UrlRouteContext routeContext)
		{
			return "http://127.0.0.1/mock";
		}
	}
}
