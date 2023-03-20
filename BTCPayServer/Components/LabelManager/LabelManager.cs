using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.LabelManager
{
    public class LabelManager : ViewComponent
    {
        public IViewComponentResult Invoke(WalletObjectId walletObjectId, string[] selectedLabels, bool excludeTypes = true, bool displayInline = false)
        {
            var vm = new LabelViewModel
            {
                ExcludeTypes = excludeTypes,
                WalletObjectId = walletObjectId,
                SelectedLabels = selectedLabels,
                DisplayInline = displayInline
            };
            return View(vm);
        }
    }
}
