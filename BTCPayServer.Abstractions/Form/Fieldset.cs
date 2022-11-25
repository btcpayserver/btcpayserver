namespace BTCPayServer.Abstractions.Form;

public class Fieldset : Field
{
    public bool Hidden { get; set; }
    public string Label { get; set; }

    public Fieldset()
    {
        Type = "fieldset";
    }
}
