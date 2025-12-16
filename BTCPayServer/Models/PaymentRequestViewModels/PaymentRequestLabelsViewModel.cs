using System.Collections.Generic;
using BTCPayServer.Services.Labels;

namespace BTCPayServer.Models.PaymentRequestViewModels;

public class PaymentRequestLabelsViewModel
{
    public string StoreId { get; set; }
    public IEnumerable<PaymentRequestLabelViewModel> Labels { get; set; }
}

public class PaymentRequestLabelViewModel
{
    public string Label { get; set; }
    public string Color { get; set; }
    public string TextColor { get; set; }
}
