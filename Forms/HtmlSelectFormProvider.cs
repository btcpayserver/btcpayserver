using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Abstractions.Form;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Forms;

public class HtmlSelectFormProvider : FormComponentProviderBase
{
    public override void Register(Dictionary<string, IFormComponentProvider> typeToComponentProvider)
    {
        typeToComponentProvider.Add("select", this);
    }

    public override string View => "Forms/SelectElement";

    public override void Validate(Form form, Field field)
    {
        if (field.Required)
        {
            ValidateField<RequiredAttribute>(field);
        }
    }
}

public class SelectField : Field
{
    public List<SelectListItem> Options { get; set; }
}
