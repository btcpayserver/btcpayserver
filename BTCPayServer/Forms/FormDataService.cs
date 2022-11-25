#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Data;
using BTCPayServer.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Forms;

public class FormDataService
{

    public static readonly Form StaticFormEmail = new()
    {
        Fields = new List<Field>() {new HtmlInputField("Enter your email", "buyerEmail", null, true, null)}
    };

    public static readonly Form StaticFormAddress = new()
    {
        Fields = new List<Field>()
        {
            new HtmlInputField("Enter your email", "buyerEmail", null, true, null, "email"),
            new HtmlInputField("Name", "buyerName", null, true, null),
            new HtmlInputField("Address Line 1", "buyerAddress1", null, true, null),
            new HtmlInputField("Address Line 2", "buyerAddress2", null, false, null),
            new HtmlInputField("City", "buyerCity", null, true, null),
            new HtmlInputField("Postcode", "buyerZip", null, false, null),
            new HtmlInputField("State", "buyerState", null, false, null),
            new HtmlInputField("Country", "buyerCountry", null, true, null)
        }
    };
}
