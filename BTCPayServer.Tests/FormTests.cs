using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
[Trait("Integration", "Integration")]
public class FormTests : UnitTestBase
{
    public FormTests(ITestOutputHelper helper) : base(helper)
    {
    }


    [Fact(Timeout = TestUtils.TestTimeout)]
    [Trait("Integration", "Integration")]  
    public async Task CanParseForm()
    {
        using var tester = CreateServerTester();
        await tester.StartAsync();
        var user = tester.NewAccount();
        user.GrantAccess();
        var service = tester.PayTester.GetService<FormDataService>();

        var form = new Form()
        {
            Fields = new List<Field>
            {
                Field.Create("Enter your email", "item1", 1.ToString(), true, null, "email"),
                Field.Create("Name", "item2", 2.ToString(), true, null),
                Field.Create("Name", "invoice_test", 2.ToString(), true, null),
                new Field
                {
                    Name = "invoice",
                    Type = "fieldset",
                    Fields = new List<Field>
                    {
                        Field.Create("Name", "test", 3.ToString(), true, null),
                        Field.Create("Name", "item4", 4.ToString(), true, null),
                        Field.Create("Name", "item5", 5.ToString(), true, null),
                    }
                }
            }
        };
        Assert.False(service.IsFormSchemaValid(form.ToString(), out _, out _));
        form = new Form
        {
            Fields = new List<Field>
            {
                Field.Create("Enter your email", "item1", 1.ToString(), true, null, "email"),
                Field.Create("Name", "item2", 2.ToString(), true, null),
                Field.Create("Name", "invoice_item3", 2.ToString(), true, null),
                new Field
                {
                    Name = "invoice",
                    Type = "fieldset",
                    Fields = new List<Field> {Field.Create("Name", "test", 3.ToString(), true, null),}
                }
            }
        };


        Assert.True(service.IsFormSchemaValid(form.ToString(), out _, out _));
        form.ApplyValuesFromForm(new FormCollection(new Dictionary<string, StringValues>()
        {
            {"item1", new StringValues("updated")},
            {"item2", new StringValues("updated")},
            {"invoice_item3", new StringValues("updated")},
            {"invoice_test", new StringValues("updated")}
        }));
        foreach (var f in form.GetAllFields())
        {
            if (f.Field.Type == "fieldset")
                continue;
            Assert.Equal("updated", f.Field.Value);
        }

        form = new Form()
        {
            Fields = new List<Field>
            {
                Field.Create("Enter your email", "item1", 1.ToString(), true, null, "email"),
                Field.Create("Name", "item2", 2.ToString(), true, null),
                Field.Create("Name", "invoice_item3", 2.ToString(), true, null),
                new Field
                {
                    Name = "invoice",
                    Type = "fieldset",
                    Fields = new List<Field>
                    {
                        new() {Name = "test", Type = "text", Constant = true, Value = "original"}
                    }
                }
            }
        };
        form.ApplyValuesFromForm(new FormCollection(new Dictionary<string, StringValues>()
        {
            {"item1", new StringValues("updated")},
            {"item2", new StringValues("updated")},
            {"invoice_item3", new StringValues("updated")},
            {"invoice_test", new StringValues("updated")}
        }));

        foreach (var f in form.GetAllFields())
        {
            var field = f.Field;
            if (field.Type == "fieldset")
                continue;
            switch (f.FullName)
            {
                case "invoice_test":
                    Assert.Equal("original", field.Value);
                    break;
                default:
                    Assert.Equal("updated", field.Value);
                    break;
            }
        }

        form = new Form()
        {
            Fields = new List<Field>
            {
                Field.Create("Enter your email", "item1", 1.ToString(), true, null, "email"),
                Field.Create("Name", "item2", 2.ToString(), true, null),
                Field.Create("Name", "invoice_item3", 2.ToString(), true, null),
                new Field
                {
                    Name = "invoice",
                    Type = "fieldset",
                    Constant = true,
                    Fields = new List<Field>
                    {
                        new() {Name = "test", Type = "text", Value = "original"}
                    }
                }
            }
        };
        form.ApplyValuesFromForm(new FormCollection(new Dictionary<string, StringValues>()
        {
            {"item1", new StringValues("updated")},
            {"item2", new StringValues("updated")},
            {"invoice_item3", new StringValues("updated")},
            {"invoice_test", new StringValues("updated")}
        }));

        foreach (var f in form.GetAllFields())
        {
            var field = f.Field;
            if (field.Type == "fieldset")
                continue;
            switch (f.FullName)
            {
                case "invoice_test":
                    Assert.Equal("original", field.Value);
                    break;
                default:
                    Assert.Equal("updated", field.Value);
                    break;
            }
        }

        var obj = service.GetValues(form);
        Assert.Equal("original", obj["invoice"]["test"].Value<string>());
        Assert.Equal("updated", obj["invoice_item3"].Value<string>());
        Clear(form);
        service.SetValues(form, obj);
        obj = service.GetValues(form);
        Assert.Equal("original", obj["invoice"]["test"].Value<string>());
        Assert.Equal("updated", obj["invoice_item3"].Value<string>());

        form = new Form()
        {
            Fields = new List<Field>(){
                new Field
                {
                    Type = "fieldset",
                    Fields = new List<Field>
                    {
                        new() {Name = "test", Type = "text"}
                    }
                }
            }
        };
        
        service.SetValues(form, obj);
        obj = service.GetValues(form);
        Assert.Null(obj["test"].Value<string>());
        
        service.SetValues(form, new JObject { ["test"] = "hello" });
        obj = service.GetValues(form);
        Assert.Equal("hello", obj["test"].Value<string>());

        var req = service.GenerateInvoiceParametersFromForm(form);
        Assert.Null(req.Amount);
        Assert.Null(req.Currency);
        
        form.Fields.Add(new Field
        {
            Name = $"{FormDataService.InvoiceParameterPrefix}amount",
            Type = "number",
            Value = "1"
        });
        req = service.GenerateInvoiceParametersFromForm(form);
        Assert.Equal(1, req.Amount);
        
        form.Fields.Add(new Field
        {
            Name = $"{FormDataService.InvoiceParameterPrefix}amount_adjustment",
            Type = "number",
            Value = "1"
        });
        req = service.GenerateInvoiceParametersFromForm(form);
        Assert.Equal(2, req.Amount);
        form.Fields.Add(new Field
        {
            Name = $"{FormDataService.InvoiceParameterPrefix}amount_adjustment2",
            Type = "number",
            Value = "2"
        });
        form.Fields.Add(new Field
        {
            Name = $"{FormDataService.InvoiceParameterPrefix}currency",
            Type = "text",
            Value = "eur"
        });
        req = service.GenerateInvoiceParametersFromForm(form);
        Assert.Equal("eur", req.Currency);
        Assert.Equal(4, req.Amount);
        

        form.Fields.Add(new Field
        {
            Name = $"{FormDataService.InvoiceParameterPrefix}amount_multiply_adjustment",
            Type = "number",
            Value = "2"
        });
        
        req = service.GenerateInvoiceParametersFromForm(form);
        Assert.Equal(8, req.Amount);
        
        
        form.Fields.Add(new Field
        {
            Name = $"{FormDataService.InvoiceParameterPrefix}amount_multiply_adjustment1",
            Type = "number",
            Value = "2"
        });
        
        req = service.GenerateInvoiceParametersFromForm(form);
        Assert.Equal(16, req.Amount);
    }

    private void Clear(Form form)
    {
        foreach (var f in form.Fields.Where(f => !f.Constant))
            f.Value = null;
    }
}
