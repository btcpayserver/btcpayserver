using System.Collections.Generic;

namespace BTCPayServer.Abstractions.Form;

public abstract class Field
{
    // HTML5 compatible type string like "text", "textarea", "email", "password", etc. Each type is a class and may contain more fields (i.e. "select" would have options).
    public string Type;
    
    // The name of the HTML5 node. Should be used as the key for the posted data.
    public string Name;
    
    // The translated label of the field.
    public string Label;
    
    // The value field is what is currently in the DB or what the user entered, but possibly not saved yet due to validation errors.
    // If this is the first the user sees the form, then value and original value are the same. Value changes as the user starts interacting with the form.
    public string Value;

    // The original value is the value that is currently saved in the backend. A "reset" button can be used to revert back to this. Should only be set from the constructor.
    public string OriginalValue;

    // A useful note shown below the field or via a tooltip / info icon. Should be translated for the user.
    public string HelpText;
    
    // The field is considered "valid" if there are no validation errors
    public List<string> ValidationErrors = new List<string>();

    public bool Required = false;

    public bool IsValid()
    {
        return ValidationErrors.Count == 0;
    }
}
