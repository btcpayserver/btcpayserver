using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BTCPayServer.Tests.Mocks
{
    public class UrlHelperMock : IUrlHelper
    {
        readonly Uri _BaseUrl;
        public UrlHelperMock(Uri baseUrl)
        {
            _BaseUrl = baseUrl;
        }
        public ActionContext ActionContext => throw new NotImplementedException();

        public string Action(UrlActionContext actionContext)
        {
            return $"/mock";
        }

        public string Content(string contentPath)
        {
            return $"{_BaseUrl}{contentPath}";
        }

        public bool IsLocalUrl(string url)
        {
            return false;
        }

        public string Link(string routeName, object values)
        {
            return _BaseUrl.AbsoluteUri;
        }

        public string RouteUrl(UrlRouteContext routeContext)
        {
            return _BaseUrl.AbsoluteUri;
        }
    }
}
