using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Services
{
    public interface IBackgroundJobClient
    {
        void Schedule(Func<CancellationToken, Task> act, TimeSpan scheduledIn);
    }
}
