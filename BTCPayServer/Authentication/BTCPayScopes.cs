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
                new[] {new[] {BTCPayScopes.StoreManagement}, new[] {BTCPayScopes.ViewStores}});
            AddScopePolicy(options, CanManageStores,
                new[] {new[] {BTCPayScopes.StoreManagement}});
            AddScopePolicy(options, CanViewInvoices,
                new[] {new[] {BTCPayScopes.ViewInvoices}, new[] {BTCPayScopes.InvoiceManagement}});
            AddScopePolicy(options, CanCreateInvoices,
                new[] {new[] {BTCPayScopes.CreateInvoices}, new[] {BTCPayScopes.InvoiceManagement}});
            AddScopePolicy(options, CanManageInvoices,
                new[] {new[] {BTCPayScopes.InvoiceManagement}});
            AddScopePolicy(options, CanManageApps,
                new[] {new[] {BTCPayScopes.AppManagement}});
            AddScopePolicy(options, CanViewApps,
                new[] {new[] {BTCPayScopes.AppManagement}, new[] {BTCPayScopes.ViewApps}});
            AddScopePolicy(options, CanManageWallet,
                new[] {new[] {BTCPayScopes.WalletManagement}});
            AddScopePolicy(options, CanViewProfile,
                new[] {new[] {OpenIddictConstants.Scopes.Profile}});
            return options;
        }

        private static void AddScopePolicy(AuthorizationOptions options, string name,
            IEnumerable<IEnumerable<string>> scopeGroups)
        {
            options.AddPolicy(name,
                builder => builder.AddRequirements(new MultipleScopeGroupsRequirement(scopeGroups)));
        }
    }

    public class MultipleScopeGroupsRequirement :
        AuthorizationHandler<MultipleScopeGroupsRequirement>, IAuthorizationRequirement
    {
        private readonly IEnumerable<IEnumerable<string>> _ScopeGroups;

        public MultipleScopeGroupsRequirement(IEnumerable<IEnumerable<string>> scopeGroups)
        {
            _ScopeGroups = scopeGroups;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, MultipleScopeGroupsRequirement requirement)
        {
            if (_ScopeGroups.Any(scopeGroup =>
                scopeGroup.All(s => context.User.HasClaim(OpenIddictConstants.Claims.Scope, s))))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
