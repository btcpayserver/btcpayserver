using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Abstractions.Form;

namespace BTCPayServer.Forms;

public class HtmlTextareaFormProvider : FormComponentProviderBase
{
    public override void Register(Dictionary<string, IFormComponentProvider> typeToComponentProvider)
    {
        typeToComponentProvider.Add("textarea", this);
    }

    public override string View => "Forms/TextareaElement";

    public override void Validate(Form form, Field field)
    {
        if (field.Required)
            ValidateField<RequiredAttribute>(field);
        if (field.Value?.Length > MaxValueLength)
            field.ValidationErrors.Add($"The value must be at most {MaxValueLength} characters.");
    }
}
