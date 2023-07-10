using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Xml.Linq;
using BTCPayServer.Configuration;

namespace BTCPayServer.TagHelpers;


[HtmlTargetElement(Attributes = "[cheat-mode]")]
public class CheatModeTagHelper
{
    public CheatModeTagHelper(BTCPayServerOptions env)
    {
        Env = env;
    }

    public BTCPayServerOptions Env { get; }
    public bool CheatMode { get; set; }
    public void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Env.CheatMode != CheatMode)
        {
            output.SuppressOutput();
        }
    }
}
