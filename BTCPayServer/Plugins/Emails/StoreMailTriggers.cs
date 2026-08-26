using System.Collections.Generic;
using BTCPayServer.Plugins.Emails.Views;

namespace BTCPayServer.Plugins.Emails;

public static class StoreMailTriggers
{
    public const string UserJoined = "StoreUserJoined";

    public static IEnumerable<EmailTriggerViewModel> GetViewModels()
    {
        yield return new EmailTriggerViewModel
        {
            Trigger = UserJoined,
            Description = "Store: User joined",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{JoinedUser.MailboxAddress}"],
                Subject = "Welcome to {Store.Name}",
                Body = EmailsPlugin.CreateEmail("You now have access to {Store.Name} with the role {JoinedUser.Role}.")
            },
            PlaceHolders =
            [
                new("{JoinedUser.Name}", "The name of the user who joined the store"),
                new("{JoinedUser.Email}", "The email of the user who joined the store"),
                new("{JoinedUser.MailboxAddress}", "The formatted mailbox address of the user who joined the store"),
                new("{JoinedUser.Role}", "The role the user joined the store with")
            ]
        };
    }
}
