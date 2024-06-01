using System;
using System.Text;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public class GreenfieldValidationException : Exception
{
    public GreenfieldValidationException(GreenfieldValidationError[] errors) : base(BuildMessage(errors))
    {
        ValidationErrors = errors;
    }

    private static string BuildMessage(GreenfieldValidationError[] errors)
    {
        if (errors == null) throw new ArgumentNullException(nameof(errors));
        var builder = new StringBuilder();
        foreach (var error in errors)
        {
            builder.AppendLine($"{error.Path}: {error.Message}");
        }
        return builder.ToString();
    }

    public GreenfieldValidationError[] ValidationErrors { get; }
}
