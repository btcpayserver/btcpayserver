using System.Collections.Generic;

namespace BTCPayServer.Plugins.Monetization.Views;

public class SelectExistingOfferingModalViewModel
{
    public string SelectedStoreId { get; set; }
    public string SelectedOfferingId { get; set; }
    public string SelectedPlanId { get; set; }
    public List<Store> Stores { get; set; }
    public class Item
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class Offering : Item
    {
        public List<Item> Plans { get; set; }
    }

    public class Store : Item
    {
        public List<Offering> Offerings { get; set; }
    }
}
