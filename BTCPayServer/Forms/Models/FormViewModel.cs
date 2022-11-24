using BTCPayServer.Abstractions.Form;
using BTCPayServer.Data.Data;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Forms.Models;

public class FormViewModel
{
    public string RedirectUrl { get; set; }
    public FormData FormData { get; set; }
    public Form Form { get => JObject.Parse(FormData.Config).ToObject<Form>(); }
}
