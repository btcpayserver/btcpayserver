using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using AngleSharp.Common;

namespace BTCPayServer.Payments;

public class PaymentTypeRegistry
{
    
    static char[] Separators = new[] { '_', '-' };
    private readonly PaymentType[] _paymentTypes;

    public PaymentTypeRegistry(IEnumerable<PaymentType> paymentTypes)
    {
        _paymentTypes = paymentTypes.ToArray();
    }
    
    public bool TryParse(string paymentType, out PaymentType type)
    {
        type = _paymentTypes.FirstOrDefault(type1 => type1.IsPaymentType(paymentType));
        return type != null;
    }
    public PaymentType Parse(string paymentType)
    {
        if (!TryParse(paymentType, out var result))
            throw new FormatException("Invalid payment type");
        return result;
    }

    public PaymentMethodId TryParsePaymentMethod(string? str)
    {
        return TryParsePaymentMethod(str, out var result) ? result : null;
    }
    public bool TryParsePaymentMethod(string? str, [MaybeNullWhen(false)] out PaymentMethodId paymentMethodId)
    {
        str ??= "";
        paymentMethodId = null;
        var parts = str.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 2)
            return false;
        if (TryParse(parts.Last(), out var type))
        {
            paymentMethodId = new PaymentMethodId(parts[0], type);
            return true;
        }
        return false;
    }
    public PaymentMethodId ParsePaymentMethod(string str)
    {
        if (!TryParsePaymentMethod(str, out var result))
            throw new FormatException("Invalid PaymentMethodId");
        return result;
    }
}
