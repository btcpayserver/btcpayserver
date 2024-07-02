using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.TagHelpers
{
    [HtmlTargetElement(Attributes = "text-translate")]
    [HtmlTargetElement(Attributes = "html-translate")]
    public class TranslateTagHelper : TagHelper
    {
        private readonly IStringLocalizer<TranslateTagHelper> _localizer;
        private readonly Safe _safe;

        public bool TextTranslate { get; set; }
        public bool HtmlTranslate { get; set; }


        public TranslateTagHelper(
            IStringLocalizer<TranslateTagHelper> localizer,
            Safe safe)
        {
            _localizer = localizer;
            _safe = safe;
        }


        public override int Order
        {
            get
            {
                // Run this TagHelper before others
                return -10;
            }
        }

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            var originalContent = output.Content.IsModified
                ? output.Content.GetContent()
                : (await output.GetChildContentAsync()).GetContent();

            var newContent = _localizer[originalContent];
            if (TextTranslate)
                output.Content.SetContent(newContent);
            else
                output.Content.SetHtmlContent(_safe.Raw(newContent.Value));
        }
    }
}
