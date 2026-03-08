using Xunit;

namespace BTCPayServer.Tests
{
    [CollectionDefinition(nameof(NonParallelizableCollectionDefinition), DisableParallelization = true)]
    public class NonParallelizableCollectionDefinition
    {
    }
}
