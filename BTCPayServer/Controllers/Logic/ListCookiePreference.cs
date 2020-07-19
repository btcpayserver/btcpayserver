using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BTCPayServer.Controllers.Logic
{
    public class ListCookiePreference
    {
        public ListCookiePreference() { }

        public ListCookiePreference(string searchTerm, int? timezoneOffset)
        {
            SearchTerm = searchTerm;
            TimezoneOffset = timezoneOffset;
        }

        public int? TimezoneOffset { get; set; }
        public string SearchTerm { get; set; }


        public static void Parse(ControllerBase ctrl, string key, 
            ref string searchTerm, ref int? timezoneOffset)
        {
            // If the user enter an empty searchTerm, then the variable will be null and not empty string
            // but we want searchTerm to be null only if the user is browsing the page via some link
            // NOT if the user entered some empty search
            searchTerm = searchTerm is string ? searchTerm :
                         ctrl.Request.Query.ContainsKey(nameof(searchTerm)) ? string.Empty :
                         null;
            if (searchTerm is null)
            {
                if (ctrl.Request.Cookies.TryGetValue(key, out var str))
                {
                    var preferences = JsonConvert.DeserializeObject<ListCookiePreference>(str);
                    searchTerm = preferences.SearchTerm;
                    timezoneOffset = preferences.TimezoneOffset ?? 0;
                }
            }
            else
            {
                ctrl.Response.Cookies.Append(key,
                    JsonConvert.SerializeObject(new ListCookiePreference(searchTerm, timezoneOffset)));
            }
        }
    }
}
