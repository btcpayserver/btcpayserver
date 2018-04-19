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
        public enum Pages
        {
            Index, Users, Rates, Emails, Policies, Theme, Hangfire
        }

        public const string ACTIVE_PAGE_KEY = "ActivePage";
        public static void SetActivePageAndTitle(this ViewDataDictionary viewData, Pages activePage)
        {
            viewData["Title"] = activePage.ToString();
            viewData[ACTIVE_PAGE_KEY] = activePage;
        }
        
        public static string IsActivePage(this ViewDataDictionary viewData, Pages page)
        {
            var activePage = viewData[ACTIVE_PAGE_KEY] as Pages?;
            return activePage == page ? "active" : null;
        }
    }
}
