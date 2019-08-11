using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.TagHelpers
{
    
    /**
     * TagHelper that allows you to translate HTML attributes. For example if you want to translate the "title" attribute, use translate-attr="title".
     * If you want to translate multiple attributes in 1 tag, you can enter more than 1 attribute by separating them with a comma, like this: translate-attr="title,alt"
     */
    [HtmlTargetElement(Attributes = "translate-attr")]
    public class TranslateAttrTagHelper : TagHelper
    {
        // Can be passed via <xxx translate-attr="..." />. 
        // PascalCase gets translated into kebab-case.
        public string TranslateAttr { get; set; }


        public override int Order
        {
            get
            {
                // Run this TagHelper before others
                return -10;
            }
        }
//        
//        public override void Process(TagHelperContext context, TagHelperOutput output)
//        {
//        }

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            var attrsToTranslate = TranslateAttr.Split(',');
            foreach (var attrToTranslate in attrsToTranslate)
            {
                var attrs = output.Attributes;

                for (int i = 0; i < attrs.Count; i++)
                {
                    TagHelperAttribute attr = attrs[i];
                    if (attr.Name.Equals(attrToTranslate))
                    {
                        var translatedAttr = translate(attr.Value.ToString());
                        output.Attributes.SetAttribute(attrToTranslate, translatedAttr);
                    }
                }
            }
        }

        public string translate(string english)
        {
            // TODO implement translation logic here
            return english;
        }
    }
}
