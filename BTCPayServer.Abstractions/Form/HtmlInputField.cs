using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Abstractions.Form;

public class HtmlInputField : Field
{
    // The translated label of the field.
    public string Label;
    
    // The original value is the value that is currently saved in the backend. A "reset" button can be used to revert back to this. Should only be set from the constructor.
    public string OriginalValue;

    // A useful note shown below the field or via a tooltip / info icon. Should be translated for the user.
    public string HelpText;

    public HtmlInputField(string label, string name, string value, bool required, string helpText, string type = "text")
    {
        Label = label;
        Name = name;
        Value = value;
        OriginalValue = value;
        Required = required;
        HelpText = helpText;
        Type = type;
    }
    // TODO JSON parsing from string to objects again probably won't work out of the box because of the different field types.
}
