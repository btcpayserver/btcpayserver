using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.ManageViewModels
{
    public class IndexViewModel
    {
        public string Username { get; set; }


        [Required]
        [EmailAddress]
        [MaxLength(50)]
        public string Email
        {
            get; set;
        }

        public bool IsEmailConfirmed { get; set; }

    }
}
