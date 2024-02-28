#nullable  enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Reporting
{
    public record QueryContext
    {
        public QueryContext(string storeId, JObject query)
        {
            StoreId = storeId;
            Query = query;
        }
        public string StoreId { get; }
        public JObject Query { get; }
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
