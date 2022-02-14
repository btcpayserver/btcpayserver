namespace BTCPayServer.Data.FormField;

public class TextField : AbstractFormField
{
    public TextField(string name, string label, string value, string helpText, bool required) : base(name, label, "text", value, helpText, required)
    {
        
    }
}
