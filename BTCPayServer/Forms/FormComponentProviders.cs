using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Form;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Forms;

public class FormComponentProviders
{
    private readonly IEnumerable<IFormComponentProvider> _formComponentProviders;

    public Dictionary<string, IFormComponentProvider> TypeToComponentProvider = new Dictionary<string, IFormComponentProvider>();

    public FormComponentProviders(IEnumerable<IFormComponentProvider> formComponentProviders)
    {
        _formComponentProviders = formComponentProviders;
        foreach (var prov in _formComponentProviders)
            prov.Register(TypeToComponentProvider);
    }

    public bool Validate(Form form, ModelStateDictionary modelState)
    {
        foreach (var field in form.Fields)
        {
            if (TypeToComponentProvider.TryGetValue(field.Type, out var provider))
            {
                provider.Validate(form, field);
                foreach (var err in field.ValidationErrors)
                    modelState.TryAddModelError(field.Name, err);
            }
        }
        return modelState.IsValid;
    }
}
