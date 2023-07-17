using System;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.App;

public class BTCPayAppPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "BTCPay.App";
    public override string Name => "BTCPay App";
    

    public override void Execute(IApplicationBuilder applicationBuilder,
        IServiceProvider applicationBuilderApplicationServices)
    {
        applicationBuilder.UseBTCPayApp();
    }

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddBTCPayApp();
    }
}
