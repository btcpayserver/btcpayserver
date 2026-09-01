using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer;

public class BTCPayHostEnvironment
{
    [JsonProperty("deploymentType")]
    public string DeploymentType { get; set; }

    [JsonProperty("commands")]
    public string[] Commands { get; set; }
    [JsonExtensionData]
    IDictionary<string, JToken> AdditionalData { get; set; }
}

public static class HostCommands
{
    /// <summary>
    /// Returns metadata about the current deployment and its supported host commands.
    /// </summary>
    public const string Env = "env";
    /// <summary>
    /// Returns the current authorized SSH public keys so they can be displayed in the server UI.
    /// </summary>
    public const string ShowAuthorizedKeys = "showauthorizedkeys";
    /// <summary>
    /// Replaces the authorized SSH public keys with the content provided by BTCPay Server.
    /// </summary>
    public const string SetAuthorizedKeys = "setauthorizedkeys";
    /// <summary>
    /// Starts the host-side domain change process for the BTCPay Server instance.
    /// </summary>
    public const string ChangeDomain = "changedomain";
    /// <summary>
    /// Starts the host-side update process for BTCPay Server.
    /// </summary>
    public const string Update = "update";
    /// <summary>
    /// Starts cleanup of unused host resources, such as old Docker images.
    /// </summary>
    public const string Clean = "clean";
    /// <summary>
    /// Starts a host-side restart of BTCPay Server and related services.
    /// </summary>
    public const string Restart = "restart";
}
