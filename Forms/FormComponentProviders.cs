using System.Collections.Generic;
using BTCPayServer.Abstractions.Form;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Forms;

public class FormComponentProviders
{
    private readonly IEnumerable<IFormComponentProvider> _formComponentProviders;

    public Dictionary<string, IFormComponentProvider> TypeToComponentProvider = new();

    public FormComponentProviders(IEnumerable<IFormComponentProvider> formComponentProviders)
    {
        _formComponentProviders = formComponentProviders;
        foreach (var prov in _formComponentProviders)
            prov.Register(TypeToComponentProvider);
    }

    public bool Validate(Form form, ModelStateDictionary modelState)
    {
        foreach (var field in form.GetAllFields())
        {
            if (TypeToComponentProvider.TryGetValue(field.Field.Type, out var provider))
            {
                provider.Validate(form, field.Field);
                foreach (var err in field.Field.ValidationErrors)
                    modelState.TryAddModelError(field.Field.Name, err);
            }
        }
        return modelState.IsValid;
    }
}
