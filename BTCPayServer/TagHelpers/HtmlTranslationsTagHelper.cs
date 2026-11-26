using BTCPayServer.Plugins.Translations;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.TagHelpers;

[HtmlTargetElement("html", Attributes = "use-translations")]
public class HtmlTranslationsTagHelper(TranslationsAccessor translationsAccessor) : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("use-translations");
        output.Attributes.SetAttribute("lang", translationsAccessor.Current.LanguageMetadata.Code);
        if (translationsAccessor.Current.LanguageMetadata.Rtl is true)
            output.Attributes.SetAttribute("dir", "rtl");
        else
            output.Attributes.RemoveAll("dir");
    }
}
