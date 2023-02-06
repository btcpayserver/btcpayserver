using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.LabelManager
{
    public class LabelManager : ViewComponent
    {
        public IViewComponentResult Invoke(WalletObjectId walletObjectId, string[] selectedLabels)
        {
            var vm = new LabelViewModel
            {
                ObjectId = walletObjectId,
                SelectedLabels = selectedLabels
            };
            return View(vm);
        }
    }
}
