# Partner Integration Guide

How to protect the commands of **your** Revit plugin with Node.aec licensing.

You add a small piece of code to your plugin that answers one question: *"is this
product licensed on this computer?"*. Everything else — sign-in, key activation,
seat limits, talking to the server, encrypting files — is done by the
**Node.aec Connector**, a separate add-in the end user installs once.

```
 End user signs in once           Your plugin asks, offline, in < 1 ms
 ┌──────────────────────┐         ┌──────────────────────────────────┐
 │  Node.aec Connector  │  writes │  NodeAecGate.Validate("slug")    │
 │  (installed by user) │ ──────► │  reads the license file          │
 └──────────────────────┘         └──────────────────────────────────┘
          %APPDATA%\NodeAec\entitlements.lease
```

Your plugin never makes a network call, never shows a login screen, and never handles
license keys.

---

## 1. Before you start

| You need | Where to get it |
|---|---|
| Your **product slug** (e.g. `meu-plugin`) | Registered on the Node.aec platform: `https://nodeaec.com.br/products/<slug>` |
| The **Node.aec Connector** installed on your dev machine | Installer from the Node.aec team (one per Revit group: 2023-2024, 2025-2026, 2027) |
| A Node.aec account with a license for your slug | Node.aec team |

---

## 2. Copy the gate source files into your project

The gate is shipped as **source code**, not as a DLL or NuGet package. Copy these files from
[`src/NodeAec.Connector/`](../src/NodeAec.Connector/) into your project, keeping the folder
structure and the namespaces unchanged (`NodeAec.Connector.*`):

| File | What it does |
|---|---|
| `Gate/NodeAecGate.cs` | The class you call: `Validate(slug)` and `OpenConnector()` |
| `Hardware/HardwareId.cs` | Identifies the computer (the license is bound to one machine) |
| `Cryptography/LeaseSignatureVerifier.cs` | Checks the license signature (Ed25519) |
| `Storage/LeaseStorage.cs` | Reads the encrypted license file |
| `Storage/SigningKeyStore.cs` | Reads the cached public keys |
| `Models/*.cs` (all four files) | License data shapes |
| `Config/ConnectorConfig.cs` | Holds the public key used to verify the signature |
| `Diagnostics/ConnectorLog.cs` | Writes warnings to `%APPDATA%\NodeAec\connector.log` |
| `Compatibility/NetFrameworkPolyfills.cs` | **Only for Revit 2023/2024 (`net48`)** |

> [!TIP]
> Keep the namespaces as they are. Two add-ins with the same namespace in different DLLs
> do not conflict in Revit, and untouched files are easier to update when we publish a
> new version.

You don't need any other file from this repository (`Auth/`, `Client/`, `UI/`, `App.cs`).

---

## 3. Add the packages to your `.csproj`

The copied files use modern C# (file-scoped namespaces, records, nullable annotations).
Add the following to your `.csproj`, picking the block that matches the Revit years you build for:

```xml
<PropertyGroup>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
  <!-- Needed on net8/net10: DPAPI (ProtectedData) comes from the Windows Desktop runtime.
       Most Revit add-ins already have it. -->
  <UseWPF>true</UseWPF>
  <!-- Copies NuGet DLLs (BouncyCastle, etc.) next to your add-in DLL -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  <!-- net48 reference assemblies have no nullable annotations: these are false positives -->
  <NoWarn Condition="'$(TargetFramework)' == 'net48'">$(NoWarn);CS8602;CS8603;CS8604</NoWarn>
</PropertyGroup>

<!-- All Revit years -->
<ItemGroup>
  <PackageReference Include="BouncyCastle.Cryptography" Version="2.7.0" />
</ItemGroup>

<!-- Revit 2023/2024 (net48) only -->
<ItemGroup Condition="'$(TargetFramework)' == 'net48'">
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="4.7.0" />
  <PackageReference Include="System.Text.Json" Version="8.0.5" />
  <Reference Include="System.Net.Http" />
</ItemGroup>

<!-- Revit 2025/2026 (net8.0-windows) only: compile against it, never ship it -->
<ItemGroup Condition="'$(TargetFramework)' == 'net8.0-windows'">
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" ExcludeAssets="runtime" />
</ItemGroup>

<!-- Revit 2027 (net10.0-windows): nothing extra -->
```

> [!NOTE]
> If your project can't use `UseWPF`, add
> `<FrameworkReference Include="Microsoft.WindowsDesktop.App" />` for the `net8`/`net10`
> targets instead. Without one of the two, `net10.0-windows` doesn't compile
> (`ProtectedData` not found).

This setup has been compiled for `net48`, `net8.0-windows` and `net10.0-windows` (0 errors,
0 warnings) using only the files from step 2 and the examples in this guide.

**What must go in your installer, next to your plugin DLL:**

| Revit | Ship these DLLs |
|---|---|
| 2023 / 2024 | `BouncyCastle.Cryptography.dll`, `System.Security.Cryptography.ProtectedData.dll`, `System.Text.Json.dll` and its dependencies |
| 2025 / 2026 / 2027 | `BouncyCastle.Cryptography.dll` only. **Do not** ship `System.Security.Cryptography.ProtectedData.dll` — Revit already provides it. |

Never ship `RevitAPI.dll`, `RevitAPIUI.dll` or `AdWindows.dll`.

### If Revit can't find BouncyCastle or System.Text.Json

Revit normally finds DLLs that sit next to your add-in DLL. If a clean machine shows
`FileNotFoundException` / `Could not load file or assembly 'BouncyCastle.Cryptography'`
when a protected command runs, register a resolver in your `IExternalApplication`.
The Connector uses the same safety net (`App.cs`):

```csharp
static App()
{
#if NET8_0_OR_GREATER
    System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) => LoadFromAddInFolder(name);
#endif
    AppDomain.CurrentDomain.AssemblyResolve += (_, args) => LoadFromAddInFolder(new AssemblyName(args.Name));
}

private static Assembly? LoadFromAddInFolder(AssemblyName name)
{
    try
    {
        string dir = Path.GetDirectoryName(typeof(App).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        string candidate = Path.Combine(dir, $"{name.Name}.dll");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }
    catch
    {
        return null; // let Revit report the original load failure
    }
}
```

(Needs `using System; using System.IO; using System.Reflection;`.)

---

## 4. Put your buttons on the `Node.aec` tab

All Node.aec products share a single Ribbon tab called **`Node.aec`**. Create your own panel
inside it — never a separate tab:

```csharp
using System.Linq;
using Autodesk.Revit.UI;

namespace MeuPlugin;

public class App : IExternalApplication
{
    private const string TabName = "Node.aec";
    private const string PanelName = "Minhas Ferramentas"; // unique to your product

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Expected: the Connector or another Node.aec plugin already created the tab.
        }

        var panel = application.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == PanelName)
                    ?? application.CreateRibbonPanel(TabName, PanelName);

        panel.AddItem(new PushButtonData(
            "MeuPlugin_Comando",                 // unique internal name
            "Executar\nComando",                 // button text
            typeof(App).Assembly.Location,       // this DLL, wherever it is installed
            "MeuPlugin.Commands.MeuComando")     // full name of the IExternalCommand
        {
            ToolTip = "O que este comando faz.",
        });

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
```

Don't do anything slow in `OnStartup`, such as network calls or heavy file reads. Revit waits for
it before it shows the UI. Load button icons from embedded resources or local files only.

Rules:
- Use a panel name that is **unique to your product**. `Conector` is used by Node.aec.
  If another add-in already uses your panel name, the code above puts your buttons in
  their panel. If the tab ever gets duplicated, the Connector merges the copies and keeps
  only one panel per name.
- Don't call tabs or panels `License` or `Licensing`. The Connector removes those because older
  versions used them.

---

## 5. Protect your commands

Call `NodeAecGate.Validate` at the start of each paid command:

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.Gate;

namespace MeuPlugin.Commands; // must match the class name given to PushButtonData

[Transaction(TransactionMode.Manual)]
public class MeuComando : IExternalCommand
{
    private const string ProductSlug = "meu-plugin";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var check = NodeAecGate.Validate(ProductSlug);
        if (!check.IsLicensed)
        {
            ShowLicenseRequired(check.Message);
            return Result.Cancelled;
        }

        // ... your command ...
        return Result.Succeeded;
    }

    private static void ShowLicenseRequired(string reason)
    {
        var dialog = new TaskDialog("Node.aec")
        {
            MainInstruction = "Licença necessária",
            MainContent = reason, // already written for the end user — show it as-is
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir Node.aec Connector");

        if (dialog.Show() == TaskDialogResult.CommandLink1)
        {
            NodeAecGate.OpenConnector();
        }
    }
}
```

### What `check.Message` contains

When `IsLicensed` is `false`, **`check.Message` is the complete explanation for the end user**.
It names the one specific reason and tells them what to do. Don't add your own list of
possible causes around it, and don't repeat "open the Connector". The message already says
so where it applies.

| Situation | Example of `check.Message` (Portuguese, shown to the user) |
|---|---|
| User never signed in on this computer | *Nenhuma credencial do Node.aec encontrada nesta estação. Abra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.* |
| Account has no license for your slug | *O produto 'meu-plugin' não consta nas licenças ativas desta conta. Adquira ou ative no catálogo Node.aec.* |
| Trial or license expired | *A licença ou período de teste de 'Meu Plugin' expirou em 01/10/2026.* |
| Seat limit reached | *O limite de computadores simultâneos para 'Meu Plugin' foi atingido.* |
| More than 30 days offline | *O prazo de tolerância offline expirou em 01/10/2026. Conecte-se à internet para sincronizar.* |
| License file copied from another PC | *A concessão de licenças foi emitida para outra estação de trabalho (Hardware ID divergente).* |
| File tampered, corrupted, or wrong format | *A licença local não passou na verificação de segurança. Conecte-se à internet e clique em atualizar no Node.aec Connector.* |
| Computer clock set wrong | *A data da licença local é inválida. Confira a data e hora deste computador e tente novamente.* |

Messages are in Portuguese (PT-BR) only for now.

When `IsLicensed` is `true`, you can also read `check.LicenseType`, `check.ProductName` and
`check.ExpiresAt` (e.g. to show "trial ends on ..." in your UI).

> [!CAUTION]
> `check.LicenseKey` is the end user's license key. Never log it, show it, or send it
> anywhere. You don't need it to protect your commands.

### Rules for calling the gate

- **No `try/catch` needed.** `Validate` never throws, and every failure comes back as
  `IsLicensed = false`. The one exception is a packaging bug: if a dependency DLL from step 3
  is missing, .NET throws `FileNotFoundException` before the gate code runs. Testing on a
  clean machine catches that.
- **Call it once per command execution**, at the top of `Execute`. It reads and verifies the
  license file on every call (no cache). That's fast, but not free.
- **Never call it from `IExternalCommandAvailability`.** Revit evaluates availability
  constantly (on every selection or view change). Leave buttons enabled and check inside `Execute`.
- `Validate` is safe on any thread. `OpenConnector()` is not.

### About `OpenConnector()`

It opens the Connector's *Minha Conta* window. If the Connector is not installed it does
**nothing**: no error and no window. So always show the dialog first, and never rely on
`OpenConnector()` alone to tell the user something is wrong.

It creates a WPF window, so it must run on Revit's UI thread: inside `Execute`, or inside an
`ExternalEvent` handler if you call it from a modeless window of your own.

---

## 6. Package and install your plugin

The gate does not care where your plugin is installed. It reads the license from
`%APPDATA%\NodeAec\`, and it finds the Connector by name inside the Revit process. Use
whichever deployment format you already use.

| | Manifest (`.addin`) | Bundle (`.bundle`) |
|---|---|---|
| Location | `%ProgramData%\Autodesk\Revit\Addins\<year>\` (all users) or `%AppData%\Autodesk\Revit\Addins\<year>\` (current user) | `%ProgramData%\Autodesk\ApplicationPlugins\<Name>.bundle\` (all users) or `%AppData%\Autodesk\ApplicationPlugins\` (current user) |
| Multiple Revit years | One `.addin` per year folder | One bundle; `PackageContents.xml` points each Revit year (`SeriesMin`/`SeriesMax`) to its own DLL folder |
| Works with Node.aec | ✅ | ✅ |

The **Connector itself** installs under `Addins\<year>\NodeAec.Connector\`. That path is only
for the Connector, and your plugin doesn't need to be next to it.

The end user needs **both** installed: the Connector (for sign-in) and your plugin. Your
installer doesn't have to bundle the Connector. If it's missing, your commands show the
*"Nenhuma credencial…"* message, and the user gets the Connector from Node.aec.

---

## 7. Keeping the gate up to date

The copied files include the Node.aec **public key** used to verify licenses
(`Config/ConnectorConfig.cs`). Your plugin only trusts licenses signed with that key.

- **Write down which version you copied.** Note the commit hash of this repository in your
  own repo (e.g. in a comment at the top of `NodeAecGate.cs` or in your changelog).
- **If Node.aec changes its signing key or the license format**, plugins built with the old
  files will stop accepting licenses. You will need to copy the new files, rebuild and
  publish a new version of your plugin. Node.aec will announce this ahead of time to
  registered partners.
- To update, replace the copied files with the new ones. Don't merge your own edits into
  them; that's why we recommend leaving them untouched.

---

## 8. What the gate protects against, and what it doesn't

The gate stops normal users from running your plugin without a license. It stops
copying the license file to another PC, editing it, forging it, or using it after the
offline period ends. Like any license check that runs on the user's machine, it does **not**
stop a determined attacker who controls that machine:

- **Patching your DLL.** Anyone can decompile a .NET assembly and remove the
  `if (!check.IsLicensed)` branch. If that matters for your product, obfuscate your
  plugin. That's your responsibility, not the gate's.
- **Freezing the clock.** Offline, the expiry check trusts the Windows clock. A user who
  keeps the clock between the last sync and the expiry date keeps the license valid.
- **Cloning the machine ID.** A Windows administrator can copy the `MachineGuid` of a
  licensed computer to another one.
- **Reading the license.** The license file is encrypted per Windows user, so any
  program running as that user can read it, license keys included.

---

## 9. Checklist before you ship

- [ ] Builds with 0 errors for every Revit year you support.
- [ ] All your panels are on the `Node.aec` tab, with a panel name unique to your product.
- [ ] Each paid command calls `NodeAecGate.Validate("<your-slug>")` first.
- [ ] The "license required" dialog shows `check.Message` as-is.
- [ ] `check.LicenseKey` is never logged or displayed.
- [ ] `Validate` is not called from any `IExternalCommandAvailability`.
- [ ] The commit hash of the copied gate files is recorded.
- [ ] Revit 2023/2024 payload contains `System.Security.Cryptography.ProtectedData.dll`; Revit 2025+ payload does **not**.
- [ ] No `RevitAPI*.dll` / `AdWindows.dll` in your installer.
- [ ] Tested on a clean machine: before signing in (dialog appears), after signing in (command runs), and offline (command still runs).

Questions: open an issue at [github.com/nodeaec/revit-connector/issues](https://github.com/nodeaec/revit-connector/issues)
or contact the Node.aec team.
