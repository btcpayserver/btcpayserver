using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Models.NotificationViewModels
{
    public class IndexViewModel
    {
        public List<NoticeDataHolder> Items { get; set; }

        public class NoticeDataHolder
        {
            public int Id { get; set; }
            public string Body { get; set; }
            public string Level { get; set; }
            public DateTime Created { get; set; }
        }
    }
}
