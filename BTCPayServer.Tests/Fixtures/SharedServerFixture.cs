#nullable  enable
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests.Fixtures;

public class SharedServerFixture : IAsyncLifetime, ITestOutputHelper
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<ServerTester> GetServerTester(ITestOutputHelper output)
    {
        _helper = output;
        if (_tester == null)
        {
            var tb = new UnitTestBase(this);
            _tester = tb.CreateServerTester(nameof(SharedServerFixture), newDb: true);
            await _tester.StartAsync();
        }
        return _tester;
    }

    private ITestOutputHelper? _helper;
    private ServerTester? _tester;

    public Task DisposeAsync()
    {
        _tester?.Dispose();
        return Task.CompletedTask;
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
