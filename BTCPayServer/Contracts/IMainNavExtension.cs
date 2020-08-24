namespace BTCPayServer.Contracts
{
    public interface IMainNavExtension
    {
        string Partial { get; }

        public class GenericMainNavExtension: IMainNavExtension
        {
            private readonly string _partial;

            public GenericMainNavExtension(string partial)
            {
                _partial = partial;
            }

            public string Partial => _partial;
        }
    }
}
