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

        public IViewComponentResult Invoke(string location, object model)
        {
            return View(new UiExtensionPointViewModel()
            {
                Partials = _uiExtensions
                    .Where(extension =>
                        extension.Location.Equals(location, StringComparison.InvariantCultureIgnoreCase))
                    .Select(extension => extension.Partial).ToArray(),
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
