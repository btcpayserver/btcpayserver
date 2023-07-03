using System;
using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Controllers;

public class BTCPayAppPlugin : BaseBTCPayServerPlugin
{
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