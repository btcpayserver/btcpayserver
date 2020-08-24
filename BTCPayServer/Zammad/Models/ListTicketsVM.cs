using System.Collections.Generic;
using Zammad.Client.Resources;

namespace BTCPayServer.Zammad
{
    public class ListTicketsVM
    {
        public List<Ticket> Tickets { get; set; }
        public IList<TicketState> States { get; set; }
    }
}