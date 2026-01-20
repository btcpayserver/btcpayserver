using System;
using System.Collections.Generic;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.LabelManager
{
    public class LabelManager : ViewComponent
    {
        public IViewComponentResult Invoke(
            string[] selectedLabels,
            bool excludeTypes = true,
            bool displayInline = false,
            Dictionary<string, RichLabelInfo> richLabelInfo = null,
            bool autoUpdate = true,
            string selectElement = null,
            string linkedType = null,
            WalletObjectId walletObjectId = null,
            string storeId = null,
            string storeObjectId = null)
        {
            var vm = new LabelViewModel
            {
                WalletObjectId = walletObjectId,
                SelectedLabels = selectedLabels,
                ExcludeTypes = excludeTypes,
                DisplayInline = displayInline,
                RichLabelInfo = richLabelInfo,
                AutoUpdate = autoUpdate,
                SelectElement = selectElement,
                LinkedType = linkedType,
                StoreId = storeId,
                StoreObjectId = storeObjectId
            };
            return View(vm);
        }
    }

    public class RichLabelInfo
    {
        public string Link { get; set; }
        public string Tooltip { get; set; }
    }
}
