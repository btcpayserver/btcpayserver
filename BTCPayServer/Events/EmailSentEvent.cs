using MimeKit;

namespace BTCPayServer.Events;

public class EmailSentEvent(string serverResponse, MimeMessage message)
{
    public string ServerResponse { get; } = serverResponse;
    public MimeMessage Message { get; } = message;
    public override string ToString() => $"Email sent ({Message.Subject})";
}
