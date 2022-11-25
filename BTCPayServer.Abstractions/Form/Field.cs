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
    // The name of the HTML5 node. Should be used as the key for the posted data.
    public string Name;

    // HTML5 compatible type string like "text", "textarea", "email", "password", etc. Each type is a class and may contain more fields (i.e. "select" would have options).
    public string Type;

    // The value field is what is currently in the DB or what the user entered, but possibly not saved yet due to validation errors.
    // If this is the first the user sees the form, then value and original value are the same. Value changes as the user starts interacting with the form.
    public string Value;

    public bool Required;
    [JsonExtensionData] public IDictionary<string, JToken> AdditionalData { get; set; }
    public List<Field> Fields { get; set; } = new();

    public virtual void Validate(ModelStateDictionary modelState)
    {
        if (Required && string.IsNullOrEmpty(Value))
        {
            modelState.AddModelError(Name, "This field is required");
        }
    }

    public bool IsValid()
    {
        ModelStateDictionary modelState = new ModelStateDictionary();
        Validate(modelState);
        return modelState.IsValid;
    }
}
