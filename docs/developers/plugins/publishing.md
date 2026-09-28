# Package and publish a plugin

You can package a plugin for direct installation on a BTCPay Server instance, or publish it through the BTCPay Server Plugin Builder so operators can discover and install it from the plugin directory.

## Package a plugin locally

[`BTCPayServer.PluginPacker`](https://github.com/btcpayserver/btcpayserver/tree/master/BTCPayServer.PluginPacker) creates the `.btcpay` archive understood by BTCPay Server. Build the plugin first, then run the packer with:

1. The directory containing the compiled plugin and its runtime dependencies.
2. The plugin assembly name, without `.dll`.
3. The directory where packages should be written.

For example, from a BTCPay Server source checkout:

```sh
dotnet build /path/to/MyPlugin/MyPlugin.csproj --configuration Release

dotnet run \
    --project BTCPayServer.PluginPacker/BTCPayServer.PluginPacker.csproj \
    -- \
    /path/to/MyPlugin/bin/Release/net10.0 \
    MyPlugin \
    /path/to/plugin-artifacts
```

The assembly name must match `MyPlugin.dll` in the build directory. Adapt the target-framework directory, such as `net10.0`, to the framework used by the referenced BTCPay Server version.

The packer loads the plugin metadata from the assembly and writes these files under `<output>/<plugin-name>/<version>/`:

- `<plugin-name>.btcpay`: The installable plugin archive.
- `<plugin-name>.btcpay.json`: The plugin manifest.
- `SHA256SUMS`: Checksums for the archive and manifest.

The archive contains the complete build directory. Use a clean Release build and inspect that directory before distributing it so development-only files or secrets are not included.

## Install a local package

Only install packages from sources you trust. Plugins execute inside the BTCPay Server process and have access to its services and data.

To install the package:

1. Sign in to BTCPay Server as a server administrator.
2. Open **Manage Plugins**.
3. Expand **Upload Plugin**.
4. Select the generated `.btcpay` file and choose **Upload**.
5. Restart BTCPay Server when prompted so it can install and load the plugin.

Use this workflow to test a package on a disposable instance compatible with the dependency condition declared by the plugin.

## Publish with Plugin Builder

Use the [BTCPay Server Plugin Builder](https://plugin-builder.btcpayserver.org/) when other operators need to install and update the plugin through BTCPay Server's plugin directory. Plugin Builder checks out the source, builds it, packages it, and hosts released versions and their metadata.

### Prepare the repository

The packer reads plugin metadata from the compiled assembly. Set the display name, description, and version in the plugin `.csproj`:

```xml
<PropertyGroup>
  <Product>My Plugin</Product>
  <Description>What the plugin does for BTCPay Server users.</Description>
  <Version>1.0.0</Version>
</PropertyGroup>
```

`BaseBTCPayServerPlugin` uses `Product` as the plugin name, `Description` as its description, and `Version` as its version. Its identifier defaults to the assembly name, which normally comes from the `.csproj` filename unless `<AssemblyName>` overrides it. The assembly name is also the second argument passed to `BTCPayServer.PluginPacker`; it is not the display name from `Product`.

Declare the supported BTCPay Server version in the plugin class:

```csharp
public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
[
    new()
    {
        Identifier = nameof(BTCPayServer),
        Condition = ">=2.4.0"
    }
];
```

Set the condition to the versions actually tested by the plugin. Add other required plugins to the same array using their identifiers and version conditions. Keep the plugin identifier and assembly name stable after the first release because installations, updates, and other plugin dependencies refer to the identifier.

Before creating the plugin in Plugin Builder:

1. Put the plugin in a publicly cloneable Git repository.
2. Verify the project metadata and dependency conditions described above.
3. Ensure a clean Release build succeeds from the committed source.
4. Add user-facing documentation explaining installation, configuration, and operation.
5. Prepare a logo, screenshots, and a demonstration video for the directory listing.
6. Run the plugin's tests and commit every file needed by the build.

### Create the plugin

1. Create an account on [Plugin Builder](https://plugin-builder.btcpayserver.org/) and confirm its email address.
2. Choose **Create a new plugin**.
3. Enter a unique slug, title, description, and optionally the initial logo and video URL.
4. Open the plugin's **Settings** and enter the Git repository URL.
5. Add the documentation and video URLs, logo, and screenshots.
6. If needed, set the Git branch or tag, the directory containing the plugin project, and the .NET build configuration. A repository containing several plugins must identify the correct plugin directory.

### Build and test a version

1. Open the plugin's **Builds** page and choose **Create a new build**.
2. Confirm the repository, Git branch or tag, plugin directory, and build configuration.
3. Start the build and review its logs, resolved commit, manifest, version, and BTCPay Server compatibility range.
4. Download the resulting pre-release package.
5. Upload that `.btcpay` file to **Manage Plugins** on a disposable compatible BTCPay Server instance, restart the server, and test installation, configuration, upgrades, and the plugin's main workflows.
6. Fix problems in the source repository and create another build rather than treating a successful package build as a runtime or security test.

### Release and list the plugin

When the tested build is ready:

1. Add release notes to the build and verify its BTCPay Server compatibility range.
2. Choose **Release**. If the plugin requires signed releases, download the manifest hash, sign it with the configured GPG key, and upload the detached signature through **Sign and Release**.
3. Open **Request Listing** if the plugin should appear in the public directory.
4. Complete the listing checklist. Plugin Builder currently requires complete plugin metadata and verified email, GitHub, and Nostr accounts for every owner.
5. Provide the requested release summary, BTCPay Server Telegram verification message, and a public review from a user who tested the plugin. An announcement date is optional.
6. Submit the listing request and address any reviewer feedback shown in its history.

Once listed and released, compatible versions become available to server administrators through BTCPay Server's plugin directory. New versions repeat the build, test, and release steps; they do not require creating another plugin entry.

The Plugin Builder interface and its validation messages are authoritative when requirements change. Its interactive automation API is available at [`/docs`](https://plugin-builder.btcpayserver.org/docs), and released plugins can be inspected in the [public plugin directory](https://plugin-builder.btcpayserver.org/public/plugins).

Publishing or listing is not a code review, security audit, or endorsement by BTCPay Server. Plugin authors remain responsible for maintenance, dependency updates, user support, licensing, release notes, and communicating supported BTCPay Server versions.
