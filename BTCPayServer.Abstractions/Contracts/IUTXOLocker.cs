using System.Collections.Generic;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Payments.PayJoin;

public interface IUTXOLocker
{
    Task<bool> TryLock(OutPoint outpoint);
    Task<bool> TryUnlock(params OutPoint[] outPoints);
    Task<bool> TryLockInputs(OutPoint[] outPoints);
    Task<HashSet<OutPoint>> FindLocks(OutPoint[] outpoints);
}
