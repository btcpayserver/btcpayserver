using System.Collections.Generic;
using BTCPayServer.Data.FormField;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

public class Form
{
    private Dictionary<string, AbstractFormField> _fields = new Dictionary<string, AbstractFormField>();
    private JObject _data;
    
    public void AddFormField(AbstractFormField field)
    {
        _fields.Add(field.Name, field);
    }

    public Dictionary<string, AbstractFormField> GetFields()
    {
        return _fields;
    }

    public bool IsValid()
    {
        foreach (KeyValuePair<string, AbstractFormField> field in _fields)
        {
            if (!field.Value.IsValid())
            {
                return false;
            }
        }
        return true;
    }

    public void SetValues(JObject data)
    {
        _data = data;
    }

    public void GetValues()
    {
        // TODO
    }
    
    public void ApplyValues()
    {
        // Reset
        foreach (KeyValuePair<string, AbstractFormField> field in _fields)
        {
            field.Value.Value = null;
        }
        
        // Apply
        foreach (var item in _data)
        {
            var field = _fields[item.Key];
            if (field != null)
            {
                _fields[item.Key].Value = item.Value?.ToString();
            }
        }
    }
}
