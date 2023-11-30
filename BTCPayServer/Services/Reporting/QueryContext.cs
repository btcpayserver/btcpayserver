#nullable  enable
using System;
using System.Collections.Generic;

namespace BTCPayServer.Services.Reporting
{
    public record QueryContext
    {
        public QueryContext(string storeId, DateTimeOffset from, DateTimeOffset to)
        {
            StoreId = storeId;
            From = from;
            To = to;
        }
        public string StoreId { get; }
        public DateTimeOffset From { get; }
        public DateTimeOffset To { get; }
        public ViewDefinition? ViewDefinition { get; set; }

        public IList<object?> AddData()
        {
            var l = CreateData();
            Data.Add(l);
            return l;
        }

        public IList<object?> CreateData()
        {
            if (ViewDefinition is null)
                throw new InvalidOperationException("You need to initialize ViewDefinition first");
            return new List<object?>(ViewDefinition.Fields.Count);
        }

        public IList<IList<object?>> Data { get; set; } = new List<IList<object?>>();
    }
}
