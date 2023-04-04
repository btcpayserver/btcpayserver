using System.Collections.Generic;
using BTCPayServer.Abstractions.Form;

namespace BTCPayServer.Forms;

public class HtmlFieldsetFormProvider : IFormComponentProvider
{
    public string View => "Forms/FieldSetElement";

    public void Register(Dictionary<string, IFormComponentProvider> typeToComponentProvider)
    {
        typeToComponentProvider.Add("fieldset", this);
    }

    public string GetValue(Form form, Field field)
    {
        return null;
    }

    public void Validate(Form form, Field field)
    {
    }
}
