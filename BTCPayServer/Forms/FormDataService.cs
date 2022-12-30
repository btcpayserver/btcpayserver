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
        Fields = new List<Field>() {Field.Create("Enter your email", "buyerEmail", null, true, null, "email")}
    };

    public static readonly Form StaticFormAddress = new()
    {
        Fields = new List<Field>()
        {
            Field.Create("Enter your email", "buyerEmail", null, true, null, "email"),
            Field.Create("Name", "buyerName", null, true, null),
            Field.Create("Address Line 1", "buyerAddress1", null, true, null),
            Field.Create("Address Line 2", "buyerAddress2", null, false, null),
            Field.Create("City", "buyerCity", null, true, null),
            Field.Create("Postcode", "buyerZip", null, false, null),
            Field.Create("State", "buyerState", null, false, null),
            Field.Create("Country", "buyerCountry", null, true, null)
        }
    };
}
