using System;
using System.Collections;
using System.Collections.Generic;

namespace BTCPayServer.Services.Reporting
{
    public record QueryContext
    {
        private readonly int _fieldsCount;

        public QueryContext(string storeId, DateTimeOffset from, DateTimeOffset to, ViewDefinition viewDefinition)
        {
            StoreId = storeId;
            From = from;
            To = to;
            ViewDefinition = viewDefinition;
        }
        public string StoreId { get; }
        public DateTimeOffset From { get; }
        public DateTimeOffset To { get; }
        public ViewDefinition ViewDefinition { get; }

        public IList<object> AddData()
        {
            var l = new List<object>(ViewDefinition.Fields.Count);
            Data.Add(l);
            return l;
        }

        public IList<IList<object>> Data { get; set; } = new List<IList<object>>();
    }
}
