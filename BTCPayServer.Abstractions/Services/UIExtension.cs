using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Abstractions.Services
{
    public class UIExtension : IUIExtension
    {
        public UIExtension(string partial, string location)
        {
            Partial = partial;
            Location = location;
        }

        public string Partial { get; }
        public string Location { get; }
    }
}
