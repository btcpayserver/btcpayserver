using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer
{
    public struct SynchronizationContextRemover : INotifyCompletion
    {
        public bool IsCompleted => SynchronizationContext.Current == null;

        public void OnCompleted(Action continuation)
        {
            var prev = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                continuation();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(prev);
            }
        }

        public SynchronizationContextRemover GetAwaiter()
        {
            return this;
        }

        public void GetResult()
        {
        }
    }
}
