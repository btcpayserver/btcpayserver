using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Abstractions.Form;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json.Linq;

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

        if (field.ValidationErrors.Count != 0 || string.IsNullOrEmpty(field.Value))
            return;

        var selectField = field as SelectField ?? JObject.FromObject(field).ToObject<SelectField>();
        if (selectField?.Options != null &&
            !selectField.Options.Any(o => string.Equals(o.Value, field.Value, System.StringComparison.Ordinal)))
        {
            field.ValidationErrors.Add($"{field.Label} contains an invalid option");
        }
    }
}

public class SelectField : Field
{
    public List<SelectListItem> Options { get; set; }
}
