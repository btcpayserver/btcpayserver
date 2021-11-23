using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace BTCPayServer.Tests
{
    [CollectionDefinition(nameof(NonParallelizableCollectionDefinition), DisableParallelization = true)]
    public class NonParallelizableCollectionDefinition
    {
    }
}
