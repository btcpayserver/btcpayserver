using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Form;

namespace BTCPayServer.Forms;

public class HtmlFieldsetFormProvider: IFormComponentProvider
{
    public string View => "Forms/FieldSetElement";

    public void Register(Dictionary<string, IFormComponentProvider> typeToComponentProvider)
    {
        typeToComponentProvider.Add("fieldset", this);
    }

    public void Validate(Field field)
    {
    }

    public void Validate(Form form, Field field)
    {
    }
}
