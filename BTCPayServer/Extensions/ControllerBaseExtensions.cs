using System;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BTCPayServer
{
    public static class ControllerBaseExtensions
    {
        public static void InvoicesQuery(this ControllerBase ctrl, ref string searchTerm, ref int? timezoneOffset)
        {
            ListCookiePreference.Parse(ctrl, "InvoicesQuery", ref searchTerm, ref timezoneOffset);
        }
        public static void PaymentRequestsQuery(this ControllerBase ctrl, ref string searchTerm, ref int? timezoneOffset)
        {
            ListCookiePreference.Parse(ctrl, "PaymentRequestsQuery", ref searchTerm, ref timezoneOffset);
        }
    }

    // Classes here remember users preferences on certain pages and store them in unified blob cookie "UserPreferCookie"
    class ListCookiePreference
    {
        internal static void Parse(ControllerBase ctrl, string propName,
            ref string searchTerm, ref int? timezoneOffset)
        {
            var prop = typeof(UserPrefsCookie).GetProperty(propName);
            var prefCookie = parsePrefCookie(ctrl);

            // If the user enter an empty searchTerm, then the variable will be null and not empty string
            // but we want searchTerm to be null only if the user is browsing the page via some link
            // NOT if the user entered some empty search
            searchTerm = searchTerm is string ? searchTerm :
                         ctrl.Request.Query.ContainsKey(nameof(searchTerm)) ? string.Empty :
                         null;
            if (searchTerm is null)
            {
                var section = prop.GetValue(prefCookie) as ListQueryDataHolder;
                if (section != null && !String.IsNullOrEmpty(section.SearchTerm))
                {
                    searchTerm = section.SearchTerm;
                    timezoneOffset = section.TimezoneOffset ?? 0;
                }
            }
            else
            {
                prop.SetValue(prefCookie, new ListQueryDataHolder(searchTerm, timezoneOffset));
                ctrl.Response.Cookies.Append(nameof(UserPrefsCookie), JsonConvert.SerializeObject(prefCookie));
            }
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
