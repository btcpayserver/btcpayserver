using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.UIExtensionPoint
{
    public class UiExtensionPoint : ViewComponent
    {
        private readonly IEnumerable<IUIExtension> _uiExtensions;

        public UiExtensionPoint(IEnumerable<IUIExtension> uiExtensions)
        {
            _uiExtensions = uiExtensions;
        }

        public IViewComponentResult Invoke(string location)
        {
            return View(_uiExtensions.Where(extension => extension.Location.Equals(location, StringComparison.InvariantCultureIgnoreCase)).Select(extension => extension.Partial));
        }
    }
}
