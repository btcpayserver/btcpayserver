using System.Collections.Generic;

namespace BTCPayServer.Abstractions.Form;

public class Fieldset
{
    public string Label { get; set; }
    public List<Field> Fields { get; set; } = new();
}
