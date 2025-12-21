using System.Collections.Generic;
using BTCPayServer.Abstractions.Form;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Forms;

public class FieldValueMirror : IFormComponentProvider
{
    public string View { get; } = null;
    public void Validate(Form form, Field field)
    {
        if (form.GetFieldByFullName(field.Value) is null)
        {
            field.ValidationErrors = new List<string> {$"{field.Name} requires {field.Value} to be present"};
        }
    }

    public void Register(Dictionary<string, IFormComponentProvider> typeToComponentProvider)
    {
        typeToComponentProvider.Add("mirror", this);
    }

    public string GetValue(Form form, Field field)
    {
        var rawValue = form.GetFieldByFullName(field.Value)?.Value;
        if (rawValue is not null && field.AdditionalData?.TryGetValue("valuemap", out var valueMap) is true &&
            valueMap is JObject map && map.TryGetValue(rawValue, out var mappedValue))
        {
            return mappedValue.Value<string>();
        }

        return rawValue;
    }

    public void SetValue(Field field, JToken value)
    {
        //ignored
    }
}
