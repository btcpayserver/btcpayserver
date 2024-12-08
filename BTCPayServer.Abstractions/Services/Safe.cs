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
        private readonly HtmlSanitizer _metaSanitizer;

        private bool isHtmlModified;

        public Safe(IHtmlHelper htmlHelper, IJsonHelper jsonHelper, HtmlSanitizer htmlSanitizer, HtmlSanitizer metaSanitizer)
        {
            _htmlHelper = htmlHelper;
            _jsonHelper = jsonHelper;
            _htmlSanitizer = htmlSanitizer;

            _metaSanitizer = metaSanitizer;
            _metaSanitizer.AllowedTags.Clear();
            _metaSanitizer.AllowedTags.Add("meta");

            _metaSanitizer.AllowedAttributes.Clear();
            _metaSanitizer.AllowedAttributes.Add("name");
            _metaSanitizer.AllowedAttributes.Add("http-equiv");
            _metaSanitizer.AllowedAttributes.Add("content");
            _metaSanitizer.AllowedAttributes.Add("value");
            _metaSanitizer.AllowedAttributes.Add("property");

            _metaSanitizer.AllowDataAttributes = false;

            _metaSanitizer.RemovingTag += (sender, e) => isHtmlModified = true;
            _metaSanitizer.RemovingAtRule += (sender, e) => isHtmlModified = true;
            _metaSanitizer.RemovingAttribute += (sender, e) => isHtmlModified = true;
            _metaSanitizer.RemovingComment += (sender, e) => isHtmlModified = true;
            _metaSanitizer.RemovingCssClass += (sender, e) => isHtmlModified = true;
            _metaSanitizer.RemovingStyle += (sender, e) => isHtmlModified = true;

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

        public string RawMeta(string inputHtml, out bool bHtmlModified)
        {
            isHtmlModified = false;

            var sRet = _metaSanitizer.Sanitize(inputHtml);
            bHtmlModified = isHtmlModified;

            return sRet;
        }
    }
}
