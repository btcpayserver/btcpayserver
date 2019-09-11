using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Xunit;
using Xunit.Sdk;

namespace BTCPayServer.Tests
{
    [CollectionDefinition("Selenium collection")]
    public class SeleniumCollection : ICollectionFixture<SeleniumTester>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}
