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

        public string RawMeta(string inputHtml, out bool isHtmlModified)
        {
             bool bHtmlModified;
             HtmlSanitizer _metaSanitizer = new HtmlSanitizer();

            _metaSanitizer.AllowedTags.Clear();
            _metaSanitizer.AllowedTags.Add("meta");

            _metaSanitizer.AllowedAttributes.Clear();
            _metaSanitizer.AllowedAttributes.Add("name");
            _metaSanitizer.AllowedAttributes.Add("http-equiv");
            _metaSanitizer.AllowedAttributes.Add("content");
            _metaSanitizer.AllowedAttributes.Add("value");
            _metaSanitizer.AllowedAttributes.Add("property");

            _metaSanitizer.AllowDataAttributes = false;

            _metaSanitizer.RemovingTag += (sender, e) => bHtmlModified = true;
            _metaSanitizer.RemovingAtRule += (sender, e) => bHtmlModified = true;
            _metaSanitizer.RemovingAttribute += (sender, e) => bHtmlModified = true;
            _metaSanitizer.RemovingComment += (sender, e) => bHtmlModified = true;
            _metaSanitizer.RemovingCssClass += (sender, e) => bHtmlModified = true;
            _metaSanitizer.RemovingStyle += (sender, e) => bHtmlModified = true;
            
            bHtmlModified = false;

            var sRet = _metaSanitizer.Sanitize(inputHtml);
            isHtmlModified = bHtmlModified;

            return sRet;
        }
    }
}
