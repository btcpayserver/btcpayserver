using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer
{
    public class CompositeDisposable : IDisposable
    {
        List<IDisposable> _Disposables = new List<IDisposable>();
        public void Add(IDisposable disposable) { _Disposables.Add(disposable); }
        public void Dispose()
        {
            foreach (var d in _Disposables)
                d.Dispose();
            _Disposables.Clear();
        }
    }
}
