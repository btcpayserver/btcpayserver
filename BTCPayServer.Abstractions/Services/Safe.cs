using System.Web;
using Ganss.Xss;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Abstractions.Services
{
    public class Safe
    {
        private readonly IHtmlHelper _htmlHelper;
        private readonly IJsonHelper _jsonHelper;
        private readonly HtmlSanitizer _htmlSanitizer;

        public Safe(IHtmlHelper htmlHelper, IJsonHelper jsonHelper, HtmlSanitizer htmlSanitizer)
        {
            _htmlHelper = htmlHelper;
            _jsonHelper = jsonHelper;
            _htmlSanitizer = htmlSanitizer;
        }

        public IHtmlContent Raw(string value)
        {
            return _htmlHelper.Raw(_htmlSanitizer.Sanitize(value));
        }
        
        public IHtmlContent RawEncode(string value)
        {
            return _htmlHelper.Raw(HttpUtility.HtmlEncode(_htmlSanitizer.Sanitize(value)));
        }

        public IHtmlContent Json(object model)
        {
            return _htmlHelper.Raw(_jsonHelper.Serialize(model));
        }
    }
}
