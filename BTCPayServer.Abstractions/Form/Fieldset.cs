using System.Collections.Generic;

namespace BTCPayServer.Abstractions.Form;

public class Fieldset
{
    public Fieldset()
    {
        this.Fields = new List<Field>();
    }

    public string Label { get; set; }
    public List<Field> Fields { get; set; }
}
