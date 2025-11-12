using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using Xunit;

namespace BTCPayServer.Tests;

public class AssertEx
{
    public static async Task<GreenfieldValidationException> AssertValidationError(string[] fields, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<GreenfieldValidationException>(act);
        foreach (var field in fields)
        {
            Assert.Contains(field, ex.ValidationErrors.Select(e => e.Path).ToArray());
        }
        return ex;
    }

    public static async Task AssertHttpError(int code, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<GreenfieldAPIException>(act);
        Assert.Equal(code, ex.HttpCode);
    }
    public static async Task AssertApiError(int httpStatus, string errorCode, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<GreenfieldAPIException>(act);
        Assert.Equal(httpStatus, ex.HttpCode);
        Assert.Equal(errorCode, ex.APIError.Code);
    }
}
