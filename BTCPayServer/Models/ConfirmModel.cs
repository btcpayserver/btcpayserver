namespace BTCPayServer.Models
{
    public class ConfirmModel
    {
        public ConfirmModel() { }

        public ConfirmModel(string title, string desc, string action = null)
        {
            Title = title;
            Description = desc;
            Action = action;
        }

        public string Title
        {
            get; set;
        }
        public string Description
        {
            get; set;
        }

        public bool DescriptionHtml { get; set; } = false;

        public string Action
        {
            get; set;
        }
        public string ButtonClass { get; set; } = "btn-danger";
        public string ActionUrl { get; set; }
    }
}
