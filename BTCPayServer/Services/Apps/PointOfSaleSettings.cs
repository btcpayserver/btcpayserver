using BTCPayServer.Plugins.PointOfSale.Models;
using PosViewType = BTCPayServer.Plugins.PointOfSale.PosViewType;

namespace BTCPayServer.Services.Apps
{
    public class PointOfSaleSettings
    {
        public PointOfSaleSettings()
        {
            Title = "Tea shop";
            Template = AppService.SerializeTemplate(new ViewPointOfSaleViewModel.Item[]
            {
                new()
                {
                    Id = "green-tea",
                    Title = "Green Tea",
                    Description =
                        "Lovely, fresh and tender, Meng Ding Gan Lu ('sweet dew') is grown in the lush Meng Ding Mountains of the southwestern province of Sichuan where it has been cultivated for over a thousand years.",
                    Image = "~/img/pos-sample/green-tea.jpg",
                    PriceType = ViewPointOfSaleViewModel.ItemPriceType.Fixed,
                    Price = 1
                },
                new()
                {
                    Id = "black-tea",
                    Title = "Black Tea",
                    Description =
                        "Tian Jian Tian Jian means 'heavenly tippy tea' in Chinese, and it describes the finest grade of dark tea. Our Tian Jian dark tea is from Hunan province which is famous for making some of the best dark teas available.",
                    Image = "~/img/pos-sample/black-tea.jpg",
                    PriceType = ViewPointOfSaleViewModel.ItemPriceType.Fixed,
                    Price = 1
                },
                new()
                {
                    Id = "rooibos",
                    Title = "Rooibos (limited)",
                    Description =
                        "Rooibos is a dramatic red tea made from a South African herb that contains polyphenols and flavonoids. Often called 'African redbush tea', Rooibos herbal tea delights the senses and delivers potential health benefits with each caffeine-free sip.",
                    Image = "~/img/pos-sample/rooibos.jpg",
                    PriceType = ViewPointOfSaleViewModel.ItemPriceType.Fixed,
                    Price = 1.2m,
                    Inventory = 5,
                },
                new()
                {
                    Id = "pu-erh",
                    Title = "Pu Erh (free)",
                    Description =
                        "This loose pur-erh tea is produced in Yunnan Province, China. The process in a relatively high humidity environment has mellowed the elemental character of the tea when compared to young Pu-erh.",
                    Image = "~/img/pos-sample/pu-erh.jpg",
                    PriceType = ViewPointOfSaleViewModel.ItemPriceType.Fixed,
                    Price = 0
                },
                new()
                {
                    Id = "herbal-tea",
                    Title = "Herbal Tea (minimum)",
                    Description =
                        "Chamomile tea is made from the flower heads of the chamomile plant. The medicinal use of chamomile dates back to the ancient Egyptians, Romans and Greeks. Pay us what you want!",
                    Image = "~/img/pos-sample/herbal-tea.jpg",
                    PriceType = ViewPointOfSaleViewModel.ItemPriceType.Minimum,
                    Price = 1.8m,
                    Disabled = false
                },
                new()
                {
                    Id = "fruit-tea",
                    Title = "Fruit Tea (any amount)",
                    Description =
                        "The Tibetan Himalayas, the land is majestic and beautiful—a spiritual place where, despite the perilous environment, many journey seeking enlightenment. Pay us what you want!",
                    Image = "~/img/pos-sample/fruit-tea.jpg",
                    PriceType = ViewPointOfSaleViewModel.ItemPriceType.Topup,
                    Disabled = false
                }
            });
            DefaultView = PosViewType.Static;
            ShowCustomAmount = false;
            ShowDiscount = false;
            ShowSearch = true;
            ShowCategories = true;
            EnableTips = false;
        }
        public string Title { get; set; }
        public string Currency { get; set; }
        public string Template { get; set; }
        public bool EnableShoppingCart { get; set; }
        public PosViewType DefaultView { get; set; }
        public bool ShowItems { get; set; }
        public bool ShowCustomAmount { get; set; }
        public bool ShowDiscount { get; set; }
        public bool ShowSearch { get; set; }
        public bool ShowCategories { get; set; }
        public bool EnableTips { get; set; }
        public string FormId { get; set; }
        public const string BUTTON_TEXT_DEF = "Buy for {0}";
        public string ButtonText { get; set; } = BUTTON_TEXT_DEF;
        public const string CUSTOM_BUTTON_TEXT_DEF = "Pay";
        public string CustomButtonText { get; set; } = CUSTOM_BUTTON_TEXT_DEF;
        public const string CUSTOM_TIP_TEXT_DEF = "Do you want to leave a tip?";
        public string CustomTipText { get; set; } = CUSTOM_TIP_TEXT_DEF;
        public static readonly int[] CUSTOM_TIP_PERCENTAGES_DEF = { 15, 18, 20 };
        public int[] CustomTipPercentages { get; set; } = CUSTOM_TIP_PERCENTAGES_DEF;
        public string Description { get; set; }
        public string NotificationUrl { get; set; }
        public string RedirectUrl { get; set; }
        public bool? RedirectAutomatically { get; set; }
    }
}
