#nullable enable
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Translations;

public record LanguageMetadata(
    [property: JsonProperty("code")] string? Code = null,
    [property: JsonProperty("rtl")] bool? Rtl = null);
