using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.TagHelpers
{
    [HtmlTargetElement(Attributes = "translate")]
    public class TranslateTagHelper : TagHelper
    {
        private readonly IStringLocalizer<TranslateTagHelper> _localizer;
        private readonly ILogger _logger;

        // Can be passed via <xxx translate="..." />
        // PascalCase gets translated into kebab-case.
        public string Translate { get; set; }


        public TranslateTagHelper(IStringLocalizer<TranslateTagHelper> localizer, ILogger<TranslateTagHelper> logger)
        {
            _localizer = localizer;
            _logger = logger;
        }


        public override int Order
        {
            get
            {
                // Run this TagHelper before others
                return -10;
            }
        }

//        public override void Process(TagHelperContext context, TagHelperOutput output)
//        {
//            //output.TagName = "a"; // Replaces <email> with <a> tag
//
//            var childContent = output.GetChildContentAsync();
//
//            var originalContent = "" + output.Content.GetContent();
//            var newContent = translate(originalContent);
//            
//            output.Content.SetContent(newContent);
//        }

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            CultureInfo currentCulture = CultureInfo.CurrentCulture;
            if (!currentCulture.TwoLetterISOLanguageName.Equals("en", System.StringComparison.Ordinal))
            {
                var originalContent = output.Content.IsModified
                    ? output.Content.GetContent()
                    : (await output.GetChildContentAsync()).GetContent();

                var newContent = _localizer[Translate];
                
                // TODO log if cannot translate
                _logger.LogInformation("Translating: "+Translate);
                
                output.Content.SetHtmlContent(newContent);
            }
        }
    }
}
