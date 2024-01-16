using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.TagHelpers
{
    [HtmlTargetElement(Attributes = "text-translate")]
    public class TranslateTagHelper : TagHelper
    {
        private readonly IStringLocalizer<TranslateTagHelper> _localizer;

        public bool TextTranslate { get; set; }


        public TranslateTagHelper(IStringLocalizer<TranslateTagHelper> localizer)
        {
            _localizer = localizer;
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
            output.Content.SetContent(newContent);
        }
    }
}
