using BTCPayServer.Abstractions.Models;
using BTCPayServer.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Forms;

public class FormsPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Forms";
    public override string Identifier => "BTCPayServer.Plugins.Forms";
    public override string Name => "Forms";
    public override string Description => "Create forms to collect additional data from customers";

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("store-category-nav", "/Plugins/Forms/Views/NavExtension.cshtml");
        services.AddSingleton<FormDataService>();
        services.AddSingleton<FormComponentProviders>();
        services.AddSingleton<IFormComponentProvider, HtmlInputFormProvider>();
        services.AddSingleton<IFormComponentProvider, HtmlTextareaFormProvider>();
        services.AddSingleton<IFormComponentProvider, HtmlFieldsetFormProvider>();
        services.AddSingleton<IFormComponentProvider, HtmlSelectFormProvider>();
        services.AddSingleton<IFormComponentProvider, FieldValueMirror>();
    }
}
