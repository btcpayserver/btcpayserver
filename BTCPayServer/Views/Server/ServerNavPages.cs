using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Views.Server
{
    public static class ServerNavPages
    {
        public static string ActivePageKey => "ActivePage";
        public static string Index => "Index";


        public static string Users => "Users";
        public static string Rates => "Rates";
        public static string Emails => "Email server";
        public static string Policies => "Policies";
        public static string Hangfire => "Hangfire";

        public static string UsersNavClass(ViewContext viewContext) => PageNavClass(viewContext, Users);
        public static string EmailsNavClass(ViewContext viewContext) => PageNavClass(viewContext, Emails);
        public static string RatesNavClass(ViewContext viewContext) => PageNavClass(viewContext, Rates);
        public static string PoliciesNavClass(ViewContext viewContext) => PageNavClass(viewContext, Policies);
        public static string HangfireNavClass(ViewContext viewContext) => PageNavClass(viewContext, Hangfire);

        public static string IndexNavClass(ViewContext viewContext) => PageNavClass(viewContext, Index);

        public static string PageNavClass(ViewContext viewContext, string page)
        {
            var activePage = viewContext.ViewData["ActivePage"] as string;
            return string.Equals(activePage, page, StringComparison.OrdinalIgnoreCase) ? "active" : null;
        }

        public static void AddActivePage(this ViewDataDictionary viewData, string activePage) => viewData[ActivePageKey] = activePage;
    }
}
