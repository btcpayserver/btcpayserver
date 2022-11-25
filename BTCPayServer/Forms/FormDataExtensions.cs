using BTCPayServer.Data.Data;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Forms;

public static class FormDataExtensions
{
    public static void AddForms(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<FormDataService>();
        serviceCollection.AddSingleton<FormComponentProviders>();
        serviceCollection.AddSingleton<IFormComponentProvider, HtmlInputFormProvider>();
        serviceCollection.AddSingleton<IFormComponentProvider, HtmlFieldsetFormProvider>();
    }

    public static string Serialize(this JObject form)
    {
        return JsonConvert.SerializeObject(form);
    }
}
