namespace BTCPayServer.Abstractions.Form;

public class TextField : Field
{
    public TextField(string label, string name, string value, bool required, string helpText)
    {
        this.Label = label;
        this.Name = name;
        this.Value = value;
        this.OriginalValue = value;
        this.Required = required;
        this.HelpText = helpText;
        this.Type = "text";
    }

    // TODO JSON parsing from string to objects again probably won't work out of the box because of the different field types. 
    

}
