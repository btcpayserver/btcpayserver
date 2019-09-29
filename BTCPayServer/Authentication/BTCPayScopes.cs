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

        public static AuthorizationOptions AddBTCPayRESTApiPolicies(this AuthorizationOptions options)
        {
            AddScopePolicy(options, CanViewStores,
                context => context.HasScopes(BTCPayScopes.StoreManagement) ||
                           context.HasScopes(BTCPayScopes.ViewStores));
            AddScopePolicy(options, CanManageStores,
                context => context.HasScopes(BTCPayScopes.StoreManagement));
            AddScopePolicy(options, CanViewInvoices,
                context => context.HasScopes(BTCPayScopes.ViewInvoices) ||
                           context.HasScopes(BTCPayScopes.InvoiceManagement));
            AddScopePolicy(options, CanCreateInvoices,
                context => context.HasScopes(BTCPayScopes.CreateInvoices) ||
                           context.HasScopes(BTCPayScopes.InvoiceManagement));
            AddScopePolicy(options, CanViewApps,
                context => context.HasScopes(BTCPayScopes.AppManagement) || context.HasScopes(BTCPayScopes.ViewApps));
            AddScopePolicy(options, CanManageInvoices,
                context => context.HasScopes(BTCPayScopes.InvoiceManagement));
            AddScopePolicy(options, CanManageApps,
                context => context.HasScopes(BTCPayScopes.AppManagement));
            AddScopePolicy(options, CanManageWallet,
                context => context.HasScopes(BTCPayScopes.WalletManagement));
            AddScopePolicy(options, CanViewProfile,
                context => context.HasScopes(OpenIddictConstants.Scopes.Profile));
            return options;
        }

        private static void AddScopePolicy(AuthorizationOptions options, string name,
            Func<AuthorizationHandlerContext, bool> scopeGroups)
        {
            options.AddPolicy(name,
                builder => builder.AddRequirements(new LambdaRequirement(scopeGroups)));
        }

        public static bool HasScopes(this AuthorizationHandlerContext context, params string[] scopes)
        {
            return scopes.All(s => context.User.HasClaim(OpenIddictConstants.Claims.Scope, s));
        }
    }

    public class LambdaRequirement :
        AuthorizationHandler<LambdaRequirement>, IAuthorizationRequirement
    {
        private readonly Func<AuthorizationHandlerContext, bool> _Func;

        public LambdaRequirement(Func<AuthorizationHandlerContext, bool> func)
        {
            _Func = func;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, LambdaRequirement requirement)
        {
            if (_Func.Invoke(context))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
