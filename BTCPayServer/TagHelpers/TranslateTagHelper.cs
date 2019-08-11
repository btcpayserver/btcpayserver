using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.TagHelpers
{
    [HtmlTargetElement(Attributes = "translate")]
    public class TranslateTagHelper : TagHelper
    {
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
            var childContent = output.Content.IsModified
                ? output.Content.GetContent()
                : (await output.GetChildContentAsync()).GetContent();

            var newContent = translate(childContent);

            output.Content.SetHtmlContent(newContent);
        }

        public string translate(string english)
        {
            // TODO implement translation logic here
            return english;
            //return english + " translated";
        }
    }
}
