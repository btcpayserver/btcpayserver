using System.Linq;
using BTCPayServer.Abstractions.Form;

namespace BTCPayServer.Forms;

public class HtmlFieldsetFormProvider: IFormComponentProvider
{
    public string CanHandle(Field field)
    {
        return new[] { "fieldset"}.Contains(field.Type) ? "Forms/FieldSetElement" : null;
    }
}