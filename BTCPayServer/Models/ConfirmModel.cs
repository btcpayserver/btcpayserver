namespace BTCPayServer.Models
{
    public class ConfirmModel
    {
        private const string ButtonClassDefault = "btn-danger";
        
        public ConfirmModel() {}

        public ConfirmModel(string title, string desc, string action = null, string buttonClass = ButtonClassDefault)
        {
            Title = title;
            Description = desc;
            Action = action;
            ButtonClass = buttonClass;
        }

        public string Title { get; set; }
        public string Description { get; set; }
        public bool DescriptionHtml { get; set; }
        public string Action { get; set; }
        public string ButtonClass { get; set; } = ButtonClassDefault;
        public string ActionUrl { get; set; }
    }
}
