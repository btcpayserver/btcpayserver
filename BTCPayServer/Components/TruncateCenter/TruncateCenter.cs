using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Components.TruncateCenter;

/// <summary>
/// Truncates long strings in the center with ellipsis: Turns e.g. a BOLT11 into "lnbcrt7…q2ns60y"
/// </summary>
/// <param name="text">The full text, e.g. a Bitcoin address or BOLT11</param>
/// <param name="link">Optional link, e.g. a block explorer URL</param>
/// <param name="classes">Optional additional CSS classes</param>
/// <param name="padding">The number of characters to show on each side</param>
/// <param name="copy">Display a copy button</param>
/// <returns>HTML with truncated string</returns>
public class TruncateCenter : ViewComponent
{
    public IViewComponentResult Invoke(string text, string link = null, string classes = null, int padding = 7, bool copy = true)
    {
        if (string.IsNullOrEmpty(text))
            return new HtmlContentViewComponentResult(new StringHtmlContent(string.Empty));
        var vm = new TruncateCenterViewModel
        {
            Classes = classes,
            Padding = padding,
            Copy = copy,
            Text = text,
            Link = link,
            Truncated = text.Length > 2 * padding ? $"{text[..padding]}…{text[^padding..]}" : text
        };
        return View(vm);
    }
}
