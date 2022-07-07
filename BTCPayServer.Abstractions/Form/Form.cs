using System.Collections.Generic;

namespace BTCPayServer.Abstractions.Form;

public class Form
{

    // Messages to be shown at the top of the form indicating user feedback like "Saved successfully" or "Please change X because of Y." or a warning, etc...
    public List<AlertMessage> TopMessages { get; set; } = new();
    
    // Groups of fields in the form
    public List<Fieldset> Fieldsets { get; set; } = new();
    

    // Are all the fields valid in the form?
    public bool IsValid()
    {
        foreach (var fieldset in Fieldsets)
        {
            foreach (var field in fieldset.Fields)
            {
                if (!field.IsValid())
                {
                    return false;
                }
            }
        }

        return true;
    }

    public Field GetFieldByName(string name)
    {
        foreach (var fieldset in Fieldsets)
        {
            foreach (var field in fieldset.Fields)
            {
                if (name.Equals(field.Name))
                {
                    return field;
                }
            }
        }
        return null;
    }

    public List<string> GetAllNames()
    {
        var names = new List<string>();
        foreach (var fieldset in Fieldsets)
        {
            foreach (var field in fieldset.Fields)
            {
                names.Add(field.Name);
            }
        }

        return names;
    }
}
