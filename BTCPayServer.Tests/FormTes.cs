using System.Collections.Generic;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests;

[Trait("Fast", "Fast")]
public class FormTests : UnitTestBase
{
    public FormTests(ITestOutputHelper helper) : base(helper)
    {
    }

    [Fact]
    public void CanParseForm()
    {
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
        var service = new FormDataService(null, null);
        Assert.False(service.IsFormSchemaValid(form.ToString(), out _, out _));
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
        foreach (string name in form.GetAllNames())
        {
            var field = form.GetFieldByName(name);
            if (field.Type == "fieldset")
                continue;
            Assert.Equal("updated", field.Value);
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
                        new() {Name = "test", Type = "text", Hidden = true, Value = "original"}
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

        foreach (string name in form.GetAllNames())
        {
            var field = form.GetFieldByName(name);
            if (field.Type == "fieldset")
                continue;
            switch (name)
            {
                case "invoice_test":
                    Assert.Equal("original", field.Value);
                    break;
                default:
                    Assert.Equal("updated", field.Value);
                    break;
            }
        }
    }
}
