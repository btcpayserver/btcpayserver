using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BTCPayServer.Blazor.VaultBridge;

public class VaultElement
{
    protected virtual void BuildRenderTree(RenderTreeBuilder builder) { }

    public RenderFragment RenderFragment => BuildRenderTree;
}
