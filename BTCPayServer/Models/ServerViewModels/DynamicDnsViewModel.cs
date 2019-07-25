using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services;

namespace BTCPayServer.Models.ServerViewModels
{
    public class DynamicDnsViewModel
    {
        public class WellKnownService
        {
            public WellKnownService(string name, string url)
            {
                Name = name;
                Url = url;
            }
            public string Name { get; set; }
            public string Url { get; set; }
        }
        public bool Modify { get; set; }
        public DynamicDnsService Settings { get; set; }
        public string LastUpdated
        {
            get
            {
                if (Settings?.LastUpdated is DateTimeOffset date)
                {
                    return Views.ViewsRazor.ToTimeAgo(date);
                }
                return null;
            }
        }
        public WellKnownService[] KnownServices { get; set; } = new []
        {
            new WellKnownService("noip", "https://dynupdate.no-ip.com/nic/update"),
            new WellKnownService("dyndns", "https://members.dyndns.org/v3/update"),
            new WellKnownService("duckdns", "https://www.duckdns.org/v3/update"),
            new WellKnownService("google", "https://domains.google.com/nic/update"),
        };
    }
}
