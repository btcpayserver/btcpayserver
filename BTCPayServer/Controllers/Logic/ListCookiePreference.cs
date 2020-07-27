using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BTCPayServer.Controllers.Logic
{
    // Classes here remember users preferences on certain pages and store them in unified blob cookie "UserPreferCookie"
    public class ListCookiePreference
    {
        public static void Parse(ControllerBase ctrl, UserPrefCookieKeys key,
            ref string searchTerm, ref int? timezoneOffset)
        {
            var prefCookie = parsePrefCookie(ctrl);

            // If the user enter an empty searchTerm, then the variable will be null and not empty string
            // but we want searchTerm to be null only if the user is browsing the page via some link
            // NOT if the user entered some empty search
            searchTerm = searchTerm is string ? searchTerm :
                         ctrl.Request.Query.ContainsKey(nameof(searchTerm)) ? string.Empty :
                         null;
            if (searchTerm is null)
            {
                var section = prefCookie.GetSection(key);
                if (section != null && !String.IsNullOrEmpty(section.SearchTerm))
                {
                    searchTerm = section.SearchTerm;
                    timezoneOffset = section.TimezoneOffset ?? 0;
                }
            }
            else
            {
                prefCookie.SetSection(key, new ListQueryDataHolder(searchTerm, timezoneOffset));
                ctrl.Response.Cookies.Append(nameof(UserPrefsCookie), JsonConvert.SerializeObject(prefCookie));
            }
        }

        private static UserPrefsCookie parsePrefCookie(ControllerBase ctrl)
        {
            var prefCookie = new UserPrefsCookie();
            ctrl.Request.Cookies.TryGetValue(nameof(UserPrefsCookie), out var strPrefCookie);
            if (!String.IsNullOrEmpty(strPrefCookie))
                prefCookie = JsonConvert.DeserializeObject<UserPrefsCookie>(strPrefCookie);

            return prefCookie;
        }
    }

    public enum UserPrefCookieKeys
    {
        InvoicesQuery, PaymentRequestsQuery
    }

    public class UserPrefsCookie
    {
        public ListQueryDataHolder InvoicesQuery { get; set; }

        public ListQueryDataHolder PaymentRequestsQuery { get; set; }

        internal ListQueryDataHolder GetSection(UserPrefCookieKeys key)
        {
            switch (key)
            {
                case UserPrefCookieKeys.InvoicesQuery:
                    return InvoicesQuery;
                case UserPrefCookieKeys.PaymentRequestsQuery:
                    return PaymentRequestsQuery;
            }

            return null;
        }

        internal void SetSection(UserPrefCookieKeys key, ListQueryDataHolder query)
        {
            switch (key)
            {
                case UserPrefCookieKeys.InvoicesQuery:
                    InvoicesQuery = query;
                    break;
                case UserPrefCookieKeys.PaymentRequestsQuery:
                    PaymentRequestsQuery = query;
                    break;
            }
        }
    }

    public class ListQueryDataHolder
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
