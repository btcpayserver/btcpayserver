using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.AppViewModels
{
    public class ViewCrowdfundViewModel
    {
        public string StatusMessage{ get; set; }
        public string StoreId { get; set; }
        public string AppId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string CustomCSSLink { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public string TargetCurrency { get; set; }
        public decimal? TargetAmount { get; set; }
        public bool EnforceTargetAmount { get; set; }

        public CrowdfundInfo Info { get; set; }


        public class CrowdfundInfo
        {
            public int TotalContributors { get; set; }
            public decimal CurrentAmount { get; set; }
            public bool Active { get; set; }
        }
    }
    
    

    public class ContributeToCrowdfund
    {
        [Required] public decimal Amount { get; set; }
        public string Email { get; set; }
    }
}
