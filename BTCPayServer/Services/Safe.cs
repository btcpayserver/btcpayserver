using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ganss.XSS;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Services
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

        public IHtmlContent Json(object model)
        {
            return _htmlHelper.Raw(_jsonHelper.Serialize(model));
        }
    }
}
