using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Abstractions.Form;

namespace BTCPayServer.Forms;

public interface IFormComponentProvider
{
    string View { get; }
    void Validate(Form form, Field field);
    void Register(Dictionary<string, IFormComponentProvider> typeToComponentProvider);
}

public abstract class FormComponentProviderBase : IFormComponentProvider
{
    public abstract string View { get; }
    public abstract void Register(Dictionary<string, IFormComponentProvider> typeToComponentProvider);
    public abstract void Validate(Form form, Field field);

    public void ValidateField<T>(Field field) where T : ValidationAttribute, new()
    {
        var result = new T().GetValidationResult(field.Value, new ValidationContext(field) { DisplayName = field.Label, MemberName = field.Name });
        if (result != null)
            field.ValidationErrors.Add(result.ErrorMessage);
    }
}
