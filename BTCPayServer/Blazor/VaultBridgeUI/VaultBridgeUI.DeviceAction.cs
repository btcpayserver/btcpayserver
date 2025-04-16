using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Hwi;
using Microsoft.JSInterop;
using NBitcoin;

namespace BTCPayServer.Blazor;

public partial class VaultBridgeUI
{
    public interface IDeviceAction
    {
        Task Run(DeviceActionContext ctx, CancellationToken cancellationToken);
    }

    public record DeviceActionContext(IServiceProvider ServiceProvider, VaultBridgeUI UI, IJSRuntime JS, HwiClient Hwi, HwiDeviceClient Device, HDFingerprint Fingerprint, BTCPayNetwork Network);
}
