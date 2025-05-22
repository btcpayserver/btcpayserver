#nullable  enable
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests.Fixtures;

public class UISharedServerFixture : IAsyncLifetime, ITestOutputHelper
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<PlaywrightTester> GetPlaywrightTester(ITestOutputHelper output)
    {
        _helper = output;
        if (_tester == null)
        {
            var tb = new UnitTestBase(this);
            _tester = tb.CreatePlaywrightTester(nameof(UISharedServerFixture), newDb: true);
            await _tester.StartAsync();
            return _tester;
        }
        else
        {
            await _tester.ResetPageContext();
            return _tester;
        }
    }

    private ITestOutputHelper? _helper;
    private PlaywrightTester? _tester;

    public async Task DisposeAsync()
    {
        if (_tester != null)
            await _tester.DisposeAsync();
    }

    void ITestOutputHelper.WriteLine(string message)
    {
        _helper?.WriteLine(message);
    }

    void ITestOutputHelper.WriteLine(string format, params object[] args)
    {
        _helper?.WriteLine(format, args);
    }
}
