using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Forms;

public class HtmlSelectFormProvider : FormComponentProviderBase
{
    public override void Register(Dictionary<string, IFormComponentProvider> typeToComponentProvider)
    {
        foreach (var t in new[] {
            "select"})
            typeToComponentProvider.Add(t, this);
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

public class SelectField: Field
{
    public List<SelectListItem> Options { get; set; }
}
