using System.IO;
using System.Text.Encodings.Web;
using System.Web;
using Ganss.Xss;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Localization;
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
        public IHtmlContent Raw(LocalizedHtmlString value)
        {
            var sw = new StringWriter();
            value.WriteTo(sw, HtmlEncoder.Default);
            return Raw(sw.ToString());
        }

        public IHtmlContent RawEncode(string value)
        {
            return _htmlHelper.Raw(HttpUtility.HtmlEncode(_htmlSanitizer.Sanitize(value)));
        }

        public IHtmlContent Json(object model)
        {
            return _htmlHelper.Raw(_jsonHelper.Serialize(model));
        }

        public IHtmlContent Meta(string inputHtml) => _htmlHelper.Raw(RawMeta(inputHtml, out _));

        public string RawMeta(string inputHtml, out bool isHtmlModified)
        {
             bool bHtmlModified;
             HtmlSanitizer sane = new HtmlSanitizer();

            sane.AllowedTags.Clear();
            sane.AllowedTags.Add("meta");

            sane.AllowedAttributes.Clear();
            sane.AllowedAttributes.Add("name");
            sane.AllowedAttributes.Add("http-equiv");
            sane.AllowedAttributes.Add("content");
            sane.AllowedAttributes.Add("value");
            sane.AllowedAttributes.Add("property");

            sane.AllowDataAttributes = false;

            sane.RemovingTag += (sender, e) => bHtmlModified = true;
            sane.RemovingAtRule += (sender, e) => bHtmlModified = true;
            sane.RemovingAttribute += (sender, e) => bHtmlModified = true;
            sane.RemovingComment += (sender, e) => bHtmlModified = true;
            sane.RemovingCssClass += (sender, e) => bHtmlModified = true;
            sane.RemovingStyle += (sender, e) => bHtmlModified = true;

            bHtmlModified = false;

            var sRet = sane.Sanitize(inputHtml);
            isHtmlModified = bHtmlModified;

            return sRet;
        }
    }
}
