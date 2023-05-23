using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using Npgsql.Internal.TypeHandlers.GeometricHandlers;

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
        if (TopMessages?.Any(t => t.Type == AlertMessage.AlertMessageType.Danger) is true)
            return false;
        return Fields.Select(f => f.IsValid()).All(o => o);
    }

    public Field GetFieldByFullName(string fullName)
    {
        foreach (var f in GetAllFields())
        {
            if (f.FullName == fullName)
                return f.Field;
        }
        return null;
    }

    public IEnumerable<(string FullName, List<string> Path, Field Field)> GetAllFields()
    {
        HashSet<string> nameReturned = new();
        foreach (var f in GetAllFieldsCore(new List<string>(), Fields))
        {
            var fullName = string.Join('_', f.Path.Where(s => !string.IsNullOrEmpty(s)));
            if (!nameReturned.Add(fullName))
                continue;
            yield return (fullName, f.Path, f.Field);
        }
    }

    public bool ValidateFieldNames(out List<string> errors)
    {
        errors = new List<string>();
        HashSet<string> nameReturned = new();
        foreach (var f in GetAllFieldsCore(new List<string>(), Fields))
        {
            var fullName = string.Join('_', f.Path.Where(s => !string.IsNullOrEmpty(s)));
            if (!nameReturned.Add(fullName))
            {
                errors.Add($"Form contains duplicate field names '{fullName}'");
            }
        }
        return errors.Count == 0;
    }

    IEnumerable<(List<string> Path, Field Field)> GetAllFieldsCore(List<string> path, List<Field> fields)
    {
        foreach (var field in fields)
        {
            List<string> thisPath = new(path.Count + 1);
            thisPath.AddRange(path);
            if (!string.IsNullOrEmpty(field.Name))
            {
                thisPath.Add(field.Name);
                yield return (thisPath, field);
            }
            foreach (var descendant in GetAllFieldsCore(thisPath, field.Fields))
            {
                descendant.Field.Constant = field.Constant || descendant.Field.Constant;
                yield return descendant;
            }
        }
    }

    public void ApplyValuesFromForm(IEnumerable<KeyValuePair<string, StringValues>> form)
    {
        var values = form.GroupBy(f => f.Key, f => f.Value).ToDictionary(g => g.Key, g => g.First());
        foreach (var f in GetAllFields())
        {
            if (f.Field.Constant || !values.TryGetValue(f.FullName, out var val))
                continue;

            f.Field.Value = val;
        }
    }

    public void SetValues(JObject values)
    {
        var fields = GetAllFields().ToDictionary(k => k.FullName, k => k.Field);
        SetValues(fields, new List<string>(), values);
    }

    private void SetValues(Dictionary<string, Field> fields, List<string> path, JObject values)
    {
        foreach (var prop in values.Properties())
        {
            List<string> propPath = new List<string>(path.Count + 1);
            propPath.AddRange(path);
            propPath.Add(prop.Name);
            if (prop.Value.Type == JTokenType.Object)
            {
                SetValues(fields, propPath, (JObject)prop.Value);
            }
            else if (prop.Value.Type == JTokenType.String)
            {
                var fullName = string.Join('_', propPath.Where(s => !string.IsNullOrEmpty(s)));
                if (fields.TryGetValue(fullName, out var f) && !f.Constant)
                    f.Value = prop.Value.Value<string>();
            }
        }
    }


}
