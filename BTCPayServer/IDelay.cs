using System;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer
{
    public interface IDelay
    {
        Task Wait(TimeSpan delay, CancellationToken cancellationToken);
    }

    public class TaskDelay : IDelay
    {
        TaskDelay()
        {

        }
        private static readonly TaskDelay _Instance = new TaskDelay();
        public static TaskDelay Instance
        {
            get
            {
                return _Instance;
            }
        }
        public Task Wait(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }
}
