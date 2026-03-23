#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Forms;

public class FormDataService
{
    public const string InvoiceParameterPrefix = "invoice_";
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly FormComponentProviders _formProviders;

    public FormDataService(
        ApplicationDbContextFactory applicationDbContextFactory,
        FormComponentProviders formProviders)
    {
        _applicationDbContextFactory = applicationDbContextFactory;
        _formProviders = formProviders;
    }

    public static readonly Form StaticFormEmail = new()
    {
        Fields = new List<Field> { Field.Create("Enter your email", "buyerEmail", null, true, null, "email") }
    };

    public static readonly Form StaticFormAddress = new()
    {
        Fields = new List<Field>
        {
            Field.Create("Enter your email", "buyerEmail", null, true, null, "email"),
            Field.Create("Name", "buyerName", null, true, null),
            Field.Create("Address Line 1", "buyerAddress1", null, true, null),
            Field.Create("Address Line 2", "buyerAddress2", null, false, null),
            Field.Create("City", "buyerCity", null, true, null),
            Field.Create("Postcode", "buyerZip", null, true, null),
            Field.Create("State", "buyerState", null, false, null),
            new SelectField
            {
                Name = "buyerCountry",
                Label = "Country",
                Required = true,
                Type = "select",
                Options = "Afghanistan, Albania, Algeria, Andorra, Angola, Antigua and Barbuda, Argentina, Armenia, Australia, Austria, Azerbaijan, The Bahamas, Bahrain, Bangladesh, Barbados, Belarus, Belgium, Belize, Benin, Bhutan, Bolivia, Bosnia and Herzegovina, Botswana, Brazil, Brunei, Bulgaria, Burkina Faso, Burundi, Cabo Verde, Cambodia, Cameroon, Canada, Central African Republic (CAR), Chad, Chile, China, Colombia, Comoros, Democratic Republic of the Congo, Republic of the Congo, Costa Rica, Cote d'Ivoire, Croatia, Cuba, Cyprus, Czech Republic, Denmark, Djibouti, Dominica, Dominican Republic, Ecuador, Egypt, El Salvador, Equatorial Guinea, Eritrea, Estonia, Eswatini (formerly Swaziland), Ethiopia, Fiji, Finland, France, Gabon, The Gambia, Georgia, Germany, Ghana, Greece, Grenada, Guatemala, Guinea, Guinea-Bissau, Guyana, Haiti, Honduras, Hungary, Iceland, India, Indonesia, Iran, Iraq, Ireland, Israel, Italy, Jamaica, Japan, Jordan, Kazakhstan, Kenya, Kiribati, Kosovo, Kuwait, Kyrgyzstan, Laos, Latvia, Lebanon, Lesotho, Liberia, Libya, Liechtenstein, Lithuania, Luxembourg, Madagascar, Malawi, Malaysia, Maldives, Mali, Malta, Marshall Islands, Mauritania, Mauritius, Mexico, Micronesia, Moldova, Monaco, Mongolia, Montenegro, Morocco, Mozambique, Myanmar (formerly Burma), Namibia, Nauru, Nepal, Netherlands, New Zealand, Nicaragua, Niger, Nigeria, North Korea, North Macedonia (formerly Macedonia), Norway, Oman, Pakistan, Palau, Palestine, Panama, Papua New Guinea, Paraguay, Peru, Philippines, Poland, Portugal, Qatar, Romania, Russia, Rwanda, Saint Kitts and Nevis, Saint Lucia, Saint Vincent and the Grenadines, Samoa, San Marino, Sao Tome and Principe, Saudi Arabia, Senegal, Serbia, Seychelles, Sierra Leone, Singapore, Slovakia, Slovenia, Solomon Islands, Somalia, South Africa, South Korea, South Sudan, Spain, Sri Lanka, Sudan, Suriname, Sweden, Switzerland, Syria, Taiwan, Tajikistan, Tanzania, Thailand, Timor-Leste (formerly East Timor), Togo, Tonga, Trinidad and Tobago, Tunisia, Turkey, Turkmenistan, Tuvalu, Uganda, Ukraine, United Arab Emirates (UAE), United Kingdom (UK), United States of America (USA), Uruguay, Uzbekistan, Vanuatu, Vatican City (Holy See), Venezuela, Vietnam, Yemen, Zambia, Zimbabwe.".Split(',').Select(s => new SelectListItem(s,s)).ToList()

            }
        }
    };

    private static readonly Dictionary<string, (string selectText, string name, Form form)> _hardcodedOptions = new()
    {
        {"", ("Do not request any information", null, null)!},
        {"Email", ("Request email address only", "Provide your email address", StaticFormEmail )},
        {"Address", ("Request shipping address", "Provide your address", StaticFormAddress)},
    };

    public async Task<SelectList> GetSelect(string storeId, string selectedFormId)
    {
        var forms = await GetForms(storeId);
        return new SelectList(_hardcodedOptions.Select(pair => new SelectListItem(pair.Value.selectText, pair.Key, selectedFormId == pair.Key)).Concat(forms.Select(data => new SelectListItem(data.Name, data.Id, data.Id == selectedFormId))),
            nameof(SelectListItem.Value), nameof(SelectListItem.Text));
    }

    public async Task<List<FormData>> GetForms(string storeId)
    {
        ArgumentNullException.ThrowIfNull(storeId);
        await using var context = _applicationDbContextFactory.CreateContext();
        return await context.Forms.Where(data => data.StoreId == storeId).ToListAsync();
    }

    public async Task<FormData?> GetForm(string storeId, string? id)
    {
        if (id is null)
        {
            return null;
        }
        await using var context = _applicationDbContextFactory.CreateContext();
        return await context.Forms.Where(data => data.Id == id && data.StoreId == storeId).FirstOrDefaultAsync();
    }
    public async Task<FormData?> GetForm(string? id)
    {
        if (id is null)
        {
            return null;
        }

        if (_hardcodedOptions.TryGetValue(id, out var hardcodedForm))
        {
            return new FormData
            {
                Config = hardcodedForm.form.ToString(),
                Id = id,
                Name = hardcodedForm.name,
                Public = false
            };
        }
        await using var context = _applicationDbContextFactory.CreateContext();
        return await context.Forms.Where(data => data.Id == id).FirstOrDefaultAsync();
    }

    public async Task RemoveForm(string id, string storeId)
    {
        await using var context = _applicationDbContextFactory.CreateContext();
        var item = await context.Forms.SingleOrDefaultAsync(data => data.StoreId == storeId && id == data.Id);
        if (item is not null)
            context.Remove(item);
        await context.SaveChangesAsync();
    }

    public async Task AddOrUpdateForm(FormData data)
    {
        await using var context = _applicationDbContextFactory.CreateContext();

        context.Update(data);
        await context.SaveChangesAsync();
    }

    public bool Validate(Form form, ModelStateDictionary modelState)
    {
        return _formProviders.Validate(form, modelState);
    }

    public bool IsFormSchemaValid(string schema, [MaybeNullWhen(false)] out Form form, [MaybeNullWhen(false)] out string error)
    {
        error = null;
        form = null;
        try
        {
            form = Form.Parse(schema);
            if (!form.ValidateFieldNames(out var errors))
            {
                error = errors.First();
            }
        }
        catch (Exception ex)
        {
            error = $"Form config was invalid: {ex.Message}";
        }
        return error is null && form is not null;
    }

    public CreateInvoiceRequest GenerateInvoiceParametersFromForm(Form form)
    {
        var amtRaw = GetValue(form, $"{InvoiceParameterPrefix}amount");
        var amt = string.IsNullOrEmpty(amtRaw) ? (decimal?) null : decimal.Parse(amtRaw, CultureInfo.InvariantCulture);
        foreach (var f in form.GetAllFields())
        {
            if (f.FullName.StartsWith($"{InvoiceParameterPrefix}amount_adjustment") && decimal.TryParse(GetValue(form, f.Field), out var adjustment))
            {
                if (amt is null)
                {
                    amt = adjustment;
                }
                else
                {
                    amt += adjustment;
                }
            } 
            if (f.FullName.StartsWith($"{InvoiceParameterPrefix}amount_multiply_adjustment") && decimal.TryParse(GetValue(form, f.Field), out var adjustmentM))
            {
                if (amt is not null)
                {
                    amt *= adjustmentM;
                }
            }
        }
        
        if(amt is not null)
        {
            amt = Math.Max(0, amt.Value);
        }
        return new CreateInvoiceRequest
        {
            Currency = GetValue(form, $"{InvoiceParameterPrefix}currency"),
            Amount = amt,
            Metadata = GetValues(form),
        };
    }

    public string? GetValue(Form form, string field)
    {
        return GetValue(form, form.GetFieldByFullName(field));
    }

    public string? GetValue(Form form, Field? field)
    {
        if (field is null)
        {
            return null;
        }
        return _formProviders.TypeToComponentProvider.TryGetValue(field.Type, out var formComponentProvider) ? formComponentProvider.GetValue(form, field) : field.Value;
    }

    public JObject GetValues(Form form)
    {
        var r = new JObject();

        foreach (var f in form.GetAllFields())
        {
            var node = r;
            for (int i = 0; i < f.Path.Count - 1; i++)
            {
                var p = f.Path[i];
                var child = node[p] as JObject;
                if (child is null)
                {
                    child = new JObject();
                    node[p] = child;
                }
                node = child;
            }

            node[f.Field.Name] = GetValue(form, f.FullName);
        }
        return r;
    }
    
    public void SetValues(Form form, JObject values)
    {
        
        var fields = form.GetAllFields().ToDictionary(k => k.FullName, k => k.Field);
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
                {
                    if (_formProviders.TypeToComponentProvider.TryGetValue(f.Type, out var formComponentProvider))
                    {
                        formComponentProvider.SetValue(f, prop.Value);
                    }
                }
            }
        }
    }
}
