using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Abstractions.Form;

public class AlertMessage
{
    // Corresponds to the Bootstrap CSS "alert alert-xxx" messages:
    // Success = green
    // Warning = orange
    // Danger = red
    // Info = blue
    public enum AlertMessageType
    {
        Success,
        Warning,
        Danger,
        Info
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public AlertMessageType Type;

    // The translated message to be shown to the user
    public string Message;

    public AlertMessage()
    {

    }

    public AlertMessage(AlertMessageType type, string message)
    {
        this.Type = type;
        this.Message = message;
    }
}
