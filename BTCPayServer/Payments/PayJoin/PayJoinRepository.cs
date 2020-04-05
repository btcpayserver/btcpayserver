using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer.Models;

namespace BTCPayServer.Payments.PayJoin
{
    public class PayJoinRepository
    {
        HashSet<OutPoint> _Outpoints = new HashSet<OutPoint>();
        HashSet<OutPoint> _LockedInputs = new HashSet<OutPoint>();
        public Task<bool> TryLock(OutPoint outpoint)
        {
            lock (_Outpoints)
            {
                return Task.FromResult(_Outpoints.Add(outpoint));
            }
        }

        public Task<bool> TryUnlock(params OutPoint[] outPoints)
        {
            if (outPoints.Length == 0)
                return Task.FromResult(true);
            lock (_Outpoints)
            {
                bool r = true;
                foreach (var outpoint in outPoints)
                {
                    r &= _Outpoints.Remove(outpoint);
                }
                return Task.FromResult(r);
            }
        }

        public Task<bool> TryLockInputs(OutPoint[] outPoint)
        {
            lock (_LockedInputs)
            {
                foreach (var o in outPoint)
                    if (!_LockedInputs.Add(o))
                        return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
    }
}
