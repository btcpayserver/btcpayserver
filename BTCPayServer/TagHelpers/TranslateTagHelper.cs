using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.TagHelpers
{
    [HtmlTargetElement(Attributes = "translate")]
    public class TranslateTagHelper : TagHelper
    {

        private readonly IStringLocalizer<TranslateTagHelper> _localizer;


        // Can be passed via <xxx translate="..." />
        // PascalCase gets translated into kebab-case.
        public string Translate { get; set; }
        
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
            var originalContent = output.Content.IsModified
                ? output.Content.GetContent()
                : (await output.GetChildContentAsync()).GetContent();

            var newContent = _localizer[Translate];
            
            

            output.Content.SetHtmlContent(newContent);
        }
        
    }
}
