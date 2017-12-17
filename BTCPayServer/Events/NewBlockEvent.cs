using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Events
{
    public class NewBlockEvent
    {
        public override string ToString()
        {
            return "New block";
        }
    }
}
