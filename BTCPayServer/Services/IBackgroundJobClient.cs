using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services
{
    public interface IBackgroundJobClient
    {
        void Schedule(Func<Task> act, TimeSpan zero);
    }
}
