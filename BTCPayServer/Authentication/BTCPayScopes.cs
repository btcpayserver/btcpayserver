using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;

namespace BTCPayServer.Authentication
{
    public static class RestAPIPolicies
    {
        public static class BTCPayScopes
        {
            public const string ViewStores = "view_stores";

            //Create and manage stores
            public const string StoreManagement = "store_management";

            //create and manage invoices
            public const string ViewInvoices = "view_invoices";

            //create and manage invoices
            public const string CreateInvoices = "create_invoice";

            public const string InvoiceManagement = "manage_invoices";

            //view apps
            public const string ViewApps = "view_apps";

            //create and manage apps
            public const string AppManagement = "app_management";
            public const string WalletManagement = "wallet_management";
        }

        public const string CanViewStores = nameof(CanViewStores);
        public const string CanManageStores = nameof(CanManageStores);
        public const string CanViewInvoices = nameof(CanViewInvoices);
        public const string CanCreateInvoices = nameof(CanCreateInvoices);
        public const string CanManageInvoices = nameof(CanManageInvoices);
        public const string CanManageApps = nameof(CanManageApps);
        public const string CanViewApps = nameof(CanViewApps);
        public const string CanManageWallet = nameof(CanManageWallet);
        public const string CanViewProfile = nameof(CanViewProfile);
    }
}
