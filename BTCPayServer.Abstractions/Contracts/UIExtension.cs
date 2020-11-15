namespace BTCPayServer.Contracts
{
    public class UIExtension: IUIExtension
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
