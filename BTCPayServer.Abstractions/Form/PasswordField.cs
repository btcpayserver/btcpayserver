namespace BTCPayServer.Abstractions.Form;

public class PasswordField : TextField
{
    public PasswordField(string label, string name, string value, bool required, string helpText) : base(label, name,
        value, required, helpText)
    {
        this.Type = "password";
    }
}
