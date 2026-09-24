#nullable enable
using System.Threading;

namespace BTCPayServer.Plugins.Maintenance;

public sealed class HostIntegrationState
{
    private BTCPayHostEnvironment? _current;

    public BTCPayHostEnvironment? Current => Volatile.Read(ref _current);

    internal void Update(BTCPayHostEnvironment? environment)
    {
        Volatile.Write(ref _current, environment);
    }
}
