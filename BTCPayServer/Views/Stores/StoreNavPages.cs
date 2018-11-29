using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Views.Stores
{
    public enum StoreNavPages
    {
        ActivePage, Index, Rates, Checkout, Tokens, Users, PayButton
    }
}
