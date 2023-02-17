using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Abstractions.Form;

public class Form
{
#nullable enable
    public static Form Parse(string str)
    {
        ArgumentNullException.ThrowIfNull(str);
        return JObject.Parse(str).ToObject<Form>(CamelCaseSerializerSettings.Serializer) ?? throw new InvalidOperationException("Impossible to deserialize Form");
    }
    public override string ToString()
    {
        return JObject.FromObject(this, CamelCaseSerializerSettings.Serializer).ToString(Newtonsoft.Json.Formatting.Indented);
    }
#nullable restore
    // Messages to be shown at the top of the form indicating user feedback like "Saved successfully" or "Please change X because of Y." or a warning, etc...
    public List<AlertMessage> TopMessages { get; set; } = new();

    // Groups of fields in the form
    public List<Field> Fields { get; set; } = new();

    // Are all the fields valid in the form?
    public bool IsValid()
    {
        return Fields.Select(f => f.IsValid()).All(o => o);
    }

    public Field GetFieldByName(string name)
    {
        return GetFieldByName(name, Fields, null);
    }

    private static Field GetFieldByName(string name, List<Field> fields, string prefix)
    {
        prefix ??= string.Empty;
        foreach (var field in fields)
        {
            var currentPrefix = prefix;
            if (!string.IsNullOrEmpty(field.Name))
            {

                currentPrefix = $"{prefix}{field.Name}";
                if (currentPrefix.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    return field;
                }

                currentPrefix += "_";
            }

            var subFieldResult = GetFieldByName(name, field.Fields, currentPrefix);
            if (subFieldResult is not null)
            {
                return subFieldResult;
            }

        }
        return null;
    }

    public List<string> GetAllNames()
    {
        return GetAllNames(Fields);
    }

    private static List<string> GetAllNames(List<Field> fields)
    {
        var names = new List<string>();

        foreach (var field in fields)
        {
            string prefix = string.Empty;
            if (!string.IsNullOrEmpty(field.Name))
            {
                names.Add(field.Name);
                prefix = $"{field.Name}_";
            }

            if (field.Fields.Any())
            {
                names.AddRange(GetAllNames(field.Fields).Select(s => $"{prefix}{s}"));
            }
        }

        return names;
    }

    public void ApplyValuesFromOtherForm(Form form)
    {
        foreach (var fieldset in Fields)
        {
            foreach (var field in fieldset.Fields)
            {
                field.Value = form
                    .GetFieldByName(
                        $"{(string.IsNullOrEmpty(fieldset.Name) ? string.Empty : fieldset.Name + "_")}{field.Name}")
                    ?.Value;
            }
        }
    }
    
    public void ApplyValuesFromForm(IFormCollection form, string ignorePrefix = null)
    {
        var names = GetAllNames();
        foreach (var name in names)
        {
            if (ignorePrefix is not null && name.StartsWith(ignorePrefix))
            {
                continue;
            }

            var field = GetFieldByName(name);
            if (field is null || !form.TryGetValue(name, out var val))
            {
                continue;
            }

            field.Value = val;
        }
    }

    public void SetValues(Dictionary<string, object> values)
    {
        SetValues(values, null, null);
    }
    private void SetValues(Dictionary<string, object> values, List<Field> fields = null,string prefix = null)
    {
        foreach (var v in values)
        {
            var field = GetFieldByName(v.Key, fields?? Fields, prefix);
            if (field is null )
            {
                continue;
            }

            if (field.Fields.Any())
            {
                if (v.Value is Dictionary<string, object> dict)
                {
                    SetValues(dict, field.Fields, field.Name + "_");
                }else if (v.Value is JObject jObject)
                {
                    dict = jObject.ToObject<Dictionary<string, object>>();
                    SetValues(dict, field.Fields, field.Name + "_");
                }
            }
            else
            {
                field.Value = (string) v.Value;
            }

        }
    }

    public Dictionary<string, object> GetValues()
    {
        return GetValues(Fields);
    }

    private static Dictionary<string, object> GetValues(List<Field> fields)
    {
        var result = new Dictionary<string, object>();
        foreach (Field field in fields)
        {
            var name = field.Name ?? string.Empty;
            if (field.Fields.Any())
            {
                var values = GetValues(fields);
                values.Remove(string.Empty, out var keylessValue);

                result.TryAdd(name, values);

                if (keylessValue is not Dictionary<string, object> dict)
                    continue;
                foreach (KeyValuePair<string, object> keyValuePair in dict)
                {
                    result.TryAdd(keyValuePair.Key, keyValuePair.Value);
                }
            }
            else
            {
                result.TryAdd(name, field.Value);
            }
        }

        return result;
    }
}
