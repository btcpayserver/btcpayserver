#nullable  enable
using System;

namespace BTCPayServer;

public class ActionDisposable(Action disposeAction) : IDisposable
{
    public void Dispose() => disposeAction();
}
