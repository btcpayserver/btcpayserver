using System.Collections.Generic;
using Zammad.Client.Resources;

namespace BTCPayServer.Zammad
{
    public class ViewTicketVM
    {
        public Ticket Ticket { get; set; }
        public IList<TicketArticle> TicketArticles { get; set; }
        public IList<TicketState> States { get; set; }
        public int ZammadUserId { get; set; }
    }
}