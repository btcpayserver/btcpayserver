using System.Linq;
using BTCPayServer.Abstractions.Form;

namespace BTCPayServer.Forms;

public class HtmlInputFormProvider: IFormComponentProvider
{
    public string CanHandle(Field field)
    {
        return new[] {
            "text",
            "radio",
            "checkbox",
            "password",
            "file",
            "hidden",
            "button",
            "submit",
            "color",
            "date",
            "datetime-local",
            "month",
            "week",
            "time",
            "email",
            "image",
            "number",
            "range",
            "search",
            "url",
            "tel",
            "reset"}.Contains(field.Type) ? "Forms/InputElement" : null;
    }
}