#nullable enable
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;

namespace BTCPayServer.Services;

public class UIExtensionsRegistry
{
    public UIExtensionsRegistry(IEnumerable<IUIExtension> uiExtensions)
    {
        ExtensionsByLocation = uiExtensions.OfType<UIExtension>().ToLookup(o => o.Location);
    }

    public ILookup<string, UIExtension> ExtensionsByLocation { get; }
}
