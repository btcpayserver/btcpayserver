using System.Collections.Generic;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.LabelManager
{
    public class LabelManager : ViewComponent
    {
        public IViewComponentResult Invoke(WalletObjectId walletObjectId, string[] selectedLabels, bool excludeTypes = true, bool displayInline = false, Dictionary<string, RichLabelInfo> richLabelInfo = null)
        {
            var vm = new LabelViewModel
            {
                ExcludeTypes = excludeTypes,
                WalletObjectId = walletObjectId,
                SelectedLabels = selectedLabels,
                DisplayInline = displayInline,
                RichLabelInfo = richLabelInfo
            };
            return View(vm);
        }
    }

    public class RichLabelInfo
    {
        public string Link  { get; set; }
        public string Tooltip  { get; set; }
    }
}
