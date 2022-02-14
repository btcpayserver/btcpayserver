namespace BTCPayServer.Data.FormField;

public abstract class AbstractFormField
{
    public bool Required { get; set; }
    public string Name { get; set; }
    public string Label { get; set; }
    public string Value { get; set; }
    public string Type { get; set; }
    public string HelpText { get; set; }

    protected AbstractFormField(string name, string label, string type, string value, string helpText, bool required)
    {
        Name = name;
        Label = label;
        Value = value;
        Type = type;
        HelpText = helpText;
        Required = required;
    }

    public bool IsValid()
    {
        return Required && !string.IsNullOrEmpty(Value);
    }

    public string[] GetValidationErrorMessages()
    {
        if (!IsValid())
        {
            return new[] { "Please enter a value" };
        }
        return null;
    }
}
