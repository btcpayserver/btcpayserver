using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Abstractions.Form;

public class Field
{
    public static Field Create(string label, string name, string value, bool required, string helpText, string type = "text")
    {
        return new Field
        {
            Label = label,
            Name = name,
            Value = value,
            OriginalValue = value,
            Required = required,
            HelpText = helpText,
            Type = type
        };
    }
    // The name of the HTML5 node. Should be used as the key for the posted data.
    public string Name;

    public bool Constant;

    // HTML5 compatible type string like "text", "textarea", "email", "password", etc.
    public string Type;

    public static Field CreateFieldset()
    {
        return new Field { Type = "fieldset" };
    }

    // The value field is what is currently in the DB or what the user entered, but possibly not saved yet due to validation errors.
    // If this is the first the user sees the form, then value and original value are the same. Value changes as the user starts interacting with the form.
    public string Value;

    public bool Required;

    // The translated label of the field.
    public string Label;

    // The original value is the value that is currently saved in the backend. A "reset" button can be used to revert back to this. Should only be set from the constructor.
    public string OriginalValue;

    // A useful note shown below the field or via a tooltip / info icon. Should be translated for the user.
    public string HelpText;

    [JsonExtensionData] public IDictionary<string, JToken> AdditionalData { get; set; }
    public List<Field> Fields { get; set; } = new();

    // The field is considered "valid" if there are no validation errors
    public List<string> ValidationErrors = new();

    public virtual bool IsValid()
    {
        return ValidationErrors.Count == 0 && Fields.All(field => field.IsValid());
    }
}
