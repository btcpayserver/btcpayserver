#nullable enable
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading;
using BTCPayServer.Abstractions.Form;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Reporting
{
    public abstract class ReportProvider
    {
        public virtual bool IsAvailable()
        {
            return true;
        }

        public abstract string Name { get; }
        public abstract Task Query(QueryContext queryContext, CancellationToken cancellation);

        public virtual Form GetForm()
        {
            var form = new Form();
            form.Fields.Add(new Field()
            {
                Name = "from",
                Type = "datetime-local",
                Label = "From",
                Required = false,
                Value = DateTimeOffset.UtcNow.AddDays(-30).ToString("u", CultureInfo.InvariantCulture)});
            form.Fields.Add(new Field()
            {
                Name = "to",
                Type = "datetime-local",
                Label = "To",
                Required = false,
                Value = DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)
            });
            return form;
        }

        protected static (DateTimeOffset? From, DateTimeOffset? To) GetFromTo(JObject formResponse)
        {
            DateTimeOffset? fromDateTime = null;
            DateTimeOffset? toDateTime = null;

            if (formResponse.TryGetValue("from", StringComparison.InvariantCultureIgnoreCase, out var from))
            {
                try
                {
                    fromDateTime = (DateTimeOffset)from;
                }
                catch (Exception e)
                {
                }
            }

            if (formResponse.TryGetValue("to", StringComparison.InvariantCultureIgnoreCase, out var to))
            {
                try
                {
                    toDateTime = (DateTimeOffset)to;
                }
                catch (Exception e)
                {
                }
            }

            return (fromDateTime, toDateTime);
        }
    }
}
