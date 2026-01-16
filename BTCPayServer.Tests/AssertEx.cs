using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
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

    public static async Task<GreenfieldAPIException> AssertPermissionError(string expectedPermission, Func<Task> act)
    {
        var err = await Assert.ThrowsAsync<GreenfieldAPIException>(async () => await act());
        var err2 = Assert.IsType<GreenfieldPermissionAPIError>(err.APIError);
        Assert.Equal(expectedPermission, err2.MissingPermission);
        return err;
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

    public static async Task<GreenfieldAPIException> AssertApiError(string expectedError, Func<Task> act)
    {
        var err = await Assert.ThrowsAsync<GreenfieldAPIException>(async () => await act());
        Assert.Equal(expectedError, err.APIError.Code);
        return err;
    }
}
