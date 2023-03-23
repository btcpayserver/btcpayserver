using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.TruncateCenter
{
    public class TruncateCenter : ViewComponent
    {
        public IViewComponentResult Invoke(string text, string link = null, string classes = null, int padding = 7, bool copy = true)
        {
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
}
