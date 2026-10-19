using BTCPayServer.Plugins;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BTCPayServer.Tests;

public class PluginExceptionHandlerTests
{
    [Fact]
    [Trait("FastTest", "FastTest")]
    public void CrashDisableFlagRequiresExplicitTrue()
    {
        var context = new DefaultHttpContext();
        Assert.False(PluginExceptionHandler.GetDisablePluginIfCrash(context));

        context.Items["DisablePluginIfCrash"] = false;
        Assert.False(PluginExceptionHandler.GetDisablePluginIfCrash(context));

        var enabledContext = new DefaultHttpContext();
        PluginExceptionHandler.SetDisablePluginIfCrash(enabledContext);
        Assert.True(PluginExceptionHandler.GetDisablePluginIfCrash(enabledContext));
    }
}
