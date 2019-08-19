using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.TagHelpers
{
    /**
     * TagHelper that allows you to translate HTML attributes. For example if you want to translate the "title" attribute, use translate-attr="title:key-for-title".
     * If you want to translate multiple attributes in 1 tag, you can enter more than 1 attribute by separating them with a comma, like this: translate-attr="title:key-for-title,alt:key-for-alt"
     */
    [HtmlTargetElement(Attributes = "translate-attr")]
    public class TranslateAttrTagHelper : TagHelper
    {
        private readonly IStringLocalizer<TranslateAttrTagHelper> _localizer;


        // Can be passed via <xxx translate-attr="..." />
        // PascalCase gets translated into kebab-case.
        public string TranslateAttr { get; set; }


        public TranslateAttrTagHelper(IStringLocalizer<TranslateAttrTagHelper> localizer)
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

        
        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            CultureInfo currentCulture = CultureInfo.CurrentCulture;
            if (!currentCulture.TwoLetterISOLanguageName.Equals("en", System.StringComparison.Ordinal))
            {
                var attrsToTranslate = TranslateAttr.Split(',');
                foreach (var attrAndKeyToTranslate in attrsToTranslate)
                {
                    var parts = attrAndKeyToTranslate.Trim().Split(":");
                    if (parts.Length == 2)
                    {
                        var attrToTranslate = parts[0];
                        var key = parts[1];

                        var attrs = output.Attributes;

                        for (int i = 0; i < attrs.Count; i++)
                        {
                            TagHelperAttribute attr = attrs[i];
                            if (attr.Name.Equals(attrToTranslate, System.StringComparison.Ordinal))
                            {
                                var originalContent = attr.Value.ToString();
                                var translation = _localizer[key];
                                output.Attributes.SetAttribute(attrToTranslate, translation);
                            }
                        }
                    }
                }
            }
        }

//        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
//        {
//        }
    }
}
