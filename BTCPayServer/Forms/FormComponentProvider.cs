using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Form;

namespace BTCPayServer.Forms;

public class FormComponentProvider : IFormComponentProvider
{
    private readonly IEnumerable<IFormComponentProvider> _formComponentProviders;

    public FormComponentProvider(IEnumerable<IFormComponentProvider> formComponentProviders)
    {
        _formComponentProviders = formComponentProviders;
    }
    
    public string CanHandle(Field field)
    {
        return _formComponentProviders.Select(formComponentProvider => formComponentProvider.CanHandle(field)).FirstOrDefault(result => !string.IsNullOrEmpty(result));
    }
}