using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Events
{
    public class NBXplorerStateChangedEvent
    {
        public NBXplorerStateChangedEvent(NBXplorerState old, NBXplorerState newState)
        {
            NewState = newState;
            OldState = old;
        }

        public NBXplorerState NewState { get; set; }
        public NBXplorerState OldState { get; set; }

        public override string ToString()
        {
            return $"NBXplorer: {OldState} => {NewState}";
        }
    }
}
