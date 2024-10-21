using System;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Abstractions.Services
{
    public class UIExtension : IUIExtension
    {
        [Obsolete("Use extension method BTCPayServer.Extensions.AddUIExtension(this IServiceCollection services, string location, string partialViewName) instead")]
        public UIExtension(string partial, string location)
        {
            Partial = partial;
            Location = location;
        }

        public string Partial { get; }
        public string Location { get; }
    }
}
