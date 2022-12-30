using BTCPayServer.Client.Models;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Services.Apps
{
    public class PointOfSaleSettings
        {
            public PointOfSaleSettings()
            {
                Title = "Tea shop";
                Template =
                    "green tea:\n" +
                    "  price: 1\n" +
                    "  title: Green Tea\n" +
                    "  description:  Lovely, fresh and tender, Meng Ding Gan Lu ('sweet dew') is grown in the lush Meng Ding Mountains of the southwestern province of Sichuan where it has been cultivated for over a thousand years.\n" +
                    "  image: ~/img/pos-sample/green-tea.jpg\n\n" +
                    "black tea:\n" +
                    "  price: 1\n" +
                    "  title: Black Tea\n" +
                    "  description: Tian Jian Tian Jian means 'heavenly tippy tea' in Chinese, and it describes the finest grade of dark tea. Our Tian Jian dark tea is from Hunan province which is famous for making some of the best dark teas available.\n" +
                    "  image: ~/img/pos-sample/black-tea.jpg\n\n" +
                    "rooibos:\n" +
                    "  price: 1.2\n" +
                    "  title: Rooibos\n" +
                    "  description: Rooibos is a dramatic red tea made from a South African herb that contains polyphenols and flavonoids. Often called 'African redbush tea', Rooibos herbal tea delights the senses and delivers potential health benefits with each caffeine-free sip.\n" +
                    "  image: ~/img/pos-sample/rooibos.jpg\n\n" +
                    "pu erh:\n" +
                    "  price: 2\n" +
                    "  title: Pu Erh\n" +
                    "  description: This loose pur-erh tea is produced in Yunnan Province, China. The process in a relatively high humidity environment has mellowed the elemental character of the tea when compared to young Pu-erh.\n" +
                    "  image: ~/img/pos-sample/pu-erh.jpg\n\n" +
                    "herbal tea:\n" +
                    "  price: 1.8\n" +
                    "  title: Herbal Tea\n" +
                    "  description: Chamomile tea is made from the flower heads of the chamomile plant. The medicinal use of chamomile dates back to the ancient Egyptians, Romans and Greeks. Pay us what you want!\n" +
                    "  image: ~/img/pos-sample/herbal-tea.jpg\n" +
                    "  custom: true\n\n" +
                    "fruit tea:\n" +
                    "  price: 1.5\n" +
                    "  title: Fruit Tea\n" +
                    "  description: The Tibetan Himalayas, the land is majestic and beautiful—a spiritual place where, despite the perilous environment, many journey seeking enlightenment. Pay us what you want!\n" +
                    "  image: ~/img/pos-sample/fruit-tea.jpg\n" +
                    "  inventory: 5\n" +
                    "  custom: true";
                DefaultView = PosViewType.Static;
                ShowCustomAmount = false;
                ShowDiscount = true;
                EnableTips = true;
                RequiresRefundEmail = RequiresRefundEmail.InheritFromStore;
            }
            public string Title { get; set; }
            public string Currency { get; set; }
            public string Template { get; set; }
            public bool EnableShoppingCart { get; set; }
            public PosViewType DefaultView { get; set; }
            public bool ShowCustomAmount { get; set; }
            public bool ShowDiscount { get; set; }
            public bool EnableTips { get; set; }
            public RequiresRefundEmail RequiresRefundEmail { get; set; }

            public string FormId { get; set; } = null;

            public const string BUTTON_TEXT_DEF = "Buy for {0}";
            public string ButtonText { get; set; } = BUTTON_TEXT_DEF;
            public const string CUSTOM_BUTTON_TEXT_DEF = "Pay";
            public string CustomButtonText { get; set; } = CUSTOM_BUTTON_TEXT_DEF;
            public const string CUSTOM_TIP_TEXT_DEF = "Do you want to leave a tip?";
            public string CustomTipText { get; set; } = CUSTOM_TIP_TEXT_DEF;
            public static readonly int[] CUSTOM_TIP_PERCENTAGES_DEF = new int[] { 15, 18, 20 };
            public int[] CustomTipPercentages { get; set; } = CUSTOM_TIP_PERCENTAGES_DEF;

            public string CustomCSSLink { get; set; }

            public string EmbeddedCSS { get; set; }

            public string Description { get; set; }
            public string NotificationUrl { get; set; }
            public string RedirectUrl { get; set; }
            public bool? RedirectAutomatically { get; set; }
            public CheckoutType CheckoutType { get; internal set; }
    }
}
