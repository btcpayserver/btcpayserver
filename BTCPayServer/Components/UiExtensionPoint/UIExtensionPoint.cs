using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.UIExtensionPoint
{
    public class UiExtensionPoint(UIExtensionsRegistry uiExtensions) : ViewComponent
    {
        public IViewComponentResult Invoke(string location, object model)
        {
            return View(new UiExtensionPointViewModel()
            {
                Partials = uiExtensions.ExtensionsByLocation[location].Select(c => c.Partial).ToArray(),
                Model = model
            });
        }
    }

    public class UiExtensionPointViewModel
    {
        public string[] Partials { get; set; }
        public object Model { get; set; }
    }
}
