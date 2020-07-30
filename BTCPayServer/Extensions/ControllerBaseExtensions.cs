using System;
using System.Reflection;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Models.PaymentRequestViewModels;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BTCPayServer
{
    // Classes here remember users preferences on certain pages and store them in unified blob cookie "UserPreferCookie"
    public static class ControllerBaseExtension
    {
        public static T ParseListQuery<T>(this ControllerBase ctrl, T model) where T : BasePagingViewModel
        {
            PropertyInfo prop;
            if (model is InvoicesModel)
                prop = typeof(UserPrefsCookie).GetProperty(nameof(UserPrefsCookie.InvoicesQuery));
            else if (model is ListPaymentRequestsViewModel)
                prop = typeof(UserPrefsCookie).GetProperty(nameof(UserPrefsCookie.PaymentRequestsQuery));
            else
                throw new Exception("Unsupported BasePagingViewModel for cookie user preferences saving");

            return ProcessParse(ctrl, model, prop);
        }

        private static T ProcessParse<T>(ControllerBase ctrl, T model, PropertyInfo prop) where T : BasePagingViewModel
        {
            var prefCookie = parsePrefCookie(ctrl);

            // If the user enter an empty searchTerm, then the variable will be null and not empty string
            // but we want searchTerm to be null only if the user is browsing the page via some link
            // NOT if the user entered some empty search
            var searchTerm = model.SearchTerm;
            searchTerm = searchTerm is string ? searchTerm :
                         ctrl.Request.Query.ContainsKey(nameof(searchTerm)) ? string.Empty :
                         null;
            if (searchTerm is null)
            {
                var section = prop.GetValue(prefCookie) as ListQueryDataHolder;
                if (section != null && !String.IsNullOrEmpty(section.SearchTerm))
                {
                    model.SearchTerm = section.SearchTerm;
                    model.TimezoneOffset = section.TimezoneOffset ?? 0;
                }
            }
            else
            {
                prop.SetValue(prefCookie, new ListQueryDataHolder(model.SearchTerm, model.TimezoneOffset));
                ctrl.Response.Cookies.Append(nameof(UserPrefsCookie), JsonConvert.SerializeObject(prefCookie));
            }

            return model;
        }

        private static UserPrefsCookie parsePrefCookie(ControllerBase ctrl)
        {
            var prefCookie = new UserPrefsCookie();
            ctrl.Request.Cookies.TryGetValue(nameof(UserPrefsCookie), out var strPrefCookie);
            if (!String.IsNullOrEmpty(strPrefCookie))
            {
                try
                {
                    prefCookie = JsonConvert.DeserializeObject<UserPrefsCookie>(strPrefCookie);
                }
                catch { /* ignore cookie deserialization failures */ }
            }

            return prefCookie;
        }

        class UserPrefsCookie
        {
            public ListQueryDataHolder InvoicesQuery { get; set; }

            public ListQueryDataHolder PaymentRequestsQuery { get; set; }
        }

        class ListQueryDataHolder
        {
            public ListQueryDataHolder() { }

            public ListQueryDataHolder(string searchTerm, int? timezoneOffset)
            {
                SearchTerm = searchTerm;
                TimezoneOffset = timezoneOffset;
            }

            public int? TimezoneOffset { get; set; }
            public string SearchTerm { get; set; }
        }
    }
}
