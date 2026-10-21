#nullable enable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Plugins;

[JsonConverter(typeof(StringEnumConverter))]
public enum PluginLifecyclePhase
{
    AssemblyLoad,
    PluginDiscovery,
    PluginActivation,
    PluginValidation,
    PluginExecution,
    Startup,
    Running,
    RuntimeRequest
}

[JsonConverter(typeof(StringEnumConverter))]
public enum PluginFailureAttribution
{
    Confirmed,
    PossiblyInvolvedCulpritUnknown
}

public class PluginFailureDiagnostic
{
    public DateTimeOffset Timestamp { get; set; }
    public string PluginIdentifier { get; set; } = string.Empty;
    public Version? PluginVersion { get; set; }
    public string BTCPayVersion { get; set; } = string.Empty;
    public PluginLifecyclePhase Phase { get; set; }
    public PluginFailureAttribution Attribution { get; set; }
    public string Details { get; set; } = string.Empty;
}
