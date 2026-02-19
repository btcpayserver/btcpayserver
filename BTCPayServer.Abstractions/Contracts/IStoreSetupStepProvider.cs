#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BTCPayServer.Abstractions.Contracts;

/// <summary>
/// Provides additional setup steps for the store dashboard setup guide.
/// Plugins implement this interface to add payment method or feature setup steps
/// alongside the built-in wallet and Lightning steps.
/// </summary>
public interface IStoreSetupStepProvider
{
    /// <summary>
    /// Returns the setup steps to display for the given store.
    /// </summary>
    Task<IEnumerable<StoreSetupStep>> GetStepsAsync(StoreSetupStepContext context);
}

/// <summary>
/// Context passed to setup step providers with store information.
/// </summary>
public class StoreSetupStepContext
{
    public string StoreId { get; set; } = string.Empty;
    public string CryptoCode { get; set; } = "BTC";
}

/// <summary>
/// Describes a single setup step in the store setup guide.
/// </summary>
public class StoreSetupStep
{
    /// <summary>Display label for the step (e.g. "Set up Monero wallet").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Relative URL to the setup page (e.g. "/stores/{storeId}/monero").</summary>
    public string? SetupUrl { get; set; }

    /// <summary>Whether this step is already completed.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Sort order. Built-in wallet=10, lightning=20. Use 30+ for plugins.</summary>
    public int Order { get; set; } = 50;

    /// <summary>SVG icon symbol name from icon-sprite.svg (e.g. "wallet-new"). Defaults to "wallet-new".</summary>
    public string IconSymbol { get; set; } = "wallet-new";
}
