using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Tests.Fixtures;
using Xunit;

namespace BTCPayServer.Tests
{
    [CollectionDefinition(nameof(NonParallelizableCollectionDefinition), DisableParallelization = true)]
    public class NonParallelizableCollectionDefinition
    {
    }

    [CollectionDefinition(nameof(UISharedServerCollection), DisableParallelization = true)]
    public class UISharedServerCollection : ICollectionFixture<UISharedServerFixture>
    {

    }
    [CollectionDefinition(nameof(SharedServerCollection), DisableParallelization = true)]
    public class SharedServerCollection : ICollectionFixture<SharedServerFixture>
    {

    }
}
