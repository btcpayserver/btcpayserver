using Microsoft.AspNetCore.Razor.TagHelpers;
using BTCPayServer.Configuration;

namespace BTCPayServer.TagHelpers;


[HtmlTargetElement(Attributes = "cheat-mode")]
public class CheatModeTagHelper : TagHelper
{
    public CheatModeTagHelper(BTCPayServerOptions env)
    {
        Env = env;
    }

    BTCPayServerOptions Env { get; }
    public bool CheatMode { get; set; }
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Env.CheatMode != CheatMode)
        {
            output.SuppressOutput();
        }
    }
}
