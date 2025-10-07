using System;

namespace BTCPayServer.Abstractions.Models
{
    public class ConfirmModel
    {
        private const string ButtonClassDefault = "btn-danger";
        public ConfirmModel() { }

        public ConfirmModel(string title, string desc, string action = null, string buttonClass = ButtonClassDefault, string actionName = null, string controllerName = null)
        {
            Title = title;
            Description = desc;
            Action = action;
            ActionName = actionName;
            ControllerName = controllerName;
            ButtonClass = buttonClass;

            if (Description.Contains("<strong>", StringComparison.InvariantCultureIgnoreCase))
            {
                DescriptionHtml = true;
            }
        }

        public bool GenerateForm { get; set; } = true;
        public bool? Antiforgery { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool DescriptionHtml { get; set; }
        public string Action { get; set; }
        public string ActionName { get; set; }
        public object ActionValues { get; set; }
        public string ControllerName { get; set; }
        public string ButtonClass { get; set; } = ButtonClassDefault;
    }
}
