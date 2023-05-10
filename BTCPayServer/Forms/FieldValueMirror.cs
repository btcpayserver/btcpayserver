using System.Collections.Generic;
using BTCPayServer.Abstractions.Form;

namespace BTCPayServer.Forms;

public class FieldValueMirror : IFormComponentProvider
{
    public string View { get; } = null;
    public void Validate(Form form, Field field)
    {
        if (form.GetFieldByFullName(field.Value) is null)
        {
            field.ValidationErrors = new List<string> { $"{field.Name} requires {field.Value} to be present" };
        }
    }

    public void Register(Dictionary<string, IFormComponentProvider> typeToComponentProvider)
    {
        typeToComponentProvider.Add("mirror", this);
    }

    public string GetValue(Form form, Field field)
    {
        return form.GetFieldByFullName(field.Value)?.Value;
    }
}
