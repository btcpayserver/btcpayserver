# Plugin API and Swagger

Plugin APIs run inside BTCPay Server. Follow the same routing, authorization, JSON, status-code, and compatibility rules as the [Greenfield API](../api/compatibility.md).

## Controllers

- Put API routes under a stable plugin-specific path to avoid collisions.
- Apply `AuthenticationSchemes.Greenfield` and a suitable existing or plugin-defined policy.
- Scope store operations through authorization, not only by accepting a `storeId` parameter.
- Return the standard validation and business-error shapes described in the API compatibility guide.

### Route ID formats

Register a regex convention when an entity ID has a stable format and is used as a route parameter:

```csharp
public override void Execute(IServiceCollection services)
{
    services.AddRegexRouteConvention("widgetId", $"^widget_{RouteRegexPatterns.Base58Characters}+\\z");
}
```

The convention applies to attribute-route parameters with the exact same name. Invalid Greenfield route values return the standard validation response; invalid values on other MVC routes return 404.

## OpenAPI

Implement `ISwaggerProvider` to merge the plugin's OpenAPI fragment into the instance document:

```csharp
public sealed class PluginSwaggerProvider(IWebHostEnvironment environment) : ISwaggerProvider
{
    public async Task<JObject> Fetch()
    {
        var file = environment.WebRootFileProvider
            .GetFileInfo("Resources/swagger/v1/swagger.example.json");
        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        return JObject.Parse(await reader.ReadToEndAsync());
    }
}
```

Register the provider as `ISwaggerProvider` and embed the JSON resource using the project settings maintained by the [plugin template](https://github.com/btcpayserver/btcpayserver-plugin-template). The host merges all providers, rewrites the server URL, and exposes the result at `/swagger/v1/swagger.json` and through `/docs`.

Use globally distinctive operation IDs, component schema names, and tags. A merge collision can overwrite another provider's document section. Validate the final merged document with the plugin installed, not only the standalone fragment.

Treat OpenAPI as part of the shipped API contract. Update it in the same change as a controller and test that documented authentication, request bodies, response schemas, and error statuses match runtime behavior.

Route regex conventions do not modify the merged OpenAPI document. Add a matching `pattern` to the plugin's path parameter schema when needed.
