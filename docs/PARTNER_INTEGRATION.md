# Partner Integration Guide

How to protect the commands of **your** Revit plugin with Node.aec licensing.

You add one NuGet package and one `if` to each paid command. Everything else is done by the
**Node.aec Connector**, a separate add-in the end user installs once: sign-in, key
activation, seat limits, talking to the server and encrypting files.

```
 End user signs in once           Your plugin asks, offline
 ┌──────────────────────┐         ┌──────────────────────────────────┐
 │  Node.aec Connector  │  writes │  Gate.Validate("meu-plugin")     │
 │  (installed by user) │ ──────► │  (NodeAec.Licensing.Lite)        │
 └──────────────────────┘         └──────────────────────────────────┘
          %APPDATA%\NodeAec\entitlements.lease
```

Your plugin never makes a network call, never shows a login screen, and never handles
license keys.

A complete working example is the [Sample Plugin](https://github.com/nodeaec/revit-sample-plugin).

---

## 1. Before you start

| You need | Where to get it |
|---|---|
| Your **product slug** (e.g. `meu-plugin`) | Registered on the Node.aec platform: `https://nodeaec.com.br/products/<slug>` |
| The **Node.aec Connector** installed on your dev machine | Installer from the Node.aec team (one per Revit group: 2023-2024, 2025-2026, 2027) |
| A Node.aec account with a license for your slug | Node.aec team |

---

## 2. Add the package

```powershell
dotnet add package NodeAec.Licensing.Lite
```

Use the latest version on [nuget.org](https://www.nuget.org/packages/NodeAec.Licensing.Lite).
The package targets `net48` (Revit 2023/2024), `net8.0-windows` (2025/2026) and
`net10.0-windows` (2027). NuGet picks the right one for your project.

Two settings in your `.csproj`:

```xml
<PropertyGroup>
  <!-- Copies the package DLLs next to your add-in DLL so they ship with it -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  <!-- Most Revit add-ins already have this. On Revit 2025+ it keeps
       System.Security.Cryptography.ProtectedData.dll out of your folder,
       because Revit already provides it. -->
  <UseWPF>true</UseWPF>
</PropertyGroup>
```

**What ends up next to your plugin DLL** (ship all of it):

| Revit | Files |
|---|---|
| 2023 / 2024 | `NodeAec.Licensing.Lite.dll`, `BouncyCastle.Cryptography.dll`, `System.Security.Cryptography.ProtectedData.dll`, `System.Text.Json.dll` and its dependencies |
| 2025 / 2026 / 2027 | `NodeAec.Licensing.Lite.dll`, `BouncyCastle.Cryptography.dll` |

Never ship `RevitAPI.dll`, `RevitAPIUI.dll`, `AdWindows.dll` or `NodeAec.Connector.dll`.

---

## 3. Protect your commands

Call `Gate.Validate` at the start of each paid command:

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Licensing;

namespace MeuPlugin.Commands; // must match the class name given to PushButtonData

[Transaction(TransactionMode.Manual)]
public class MeuComando : IExternalCommand
{
    private const string ProductSlug = "meu-plugin";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var gate = Gate.Validate(ProductSlug);
        if (!gate.IsLicensed)
        {
            ShowLicenseRequired(gate.Message);
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
            Gate.OpenConnector();
        }
    }
}
```

That's the whole integration.

### What `gate.Message` contains

When `IsLicensed` is `false`, **`gate.Message` is the complete explanation for the end
user**. It names the one specific reason and tells them what to do. Show it as-is.
Don't add a list of possible causes around it, because the user can't tell which one
applies, and don't repeat "open the Connector". The message already says so where it applies.

| Situation | Example of `gate.Message` (Portuguese, shown to the user) |
|---|---|
| User never signed in on this computer, or the Connector is not installed | *Nenhuma credencial do Node.aec encontrada nesta estação. Abra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.* |
| Account has no license for your slug | *O produto 'meu-plugin' não consta nas licenças ativas desta conta. Adquira ou ative no catálogo Node.aec.* |
| Trial or license expired | *A licença ou período de teste de 'Meu Plugin' expirou em 01/10/2026.* |
| Seat limit reached | *O limite de computadores simultâneos para 'Meu Plugin' foi atingido.* |
| More than 30 days offline | *O prazo de tolerância offline expirou em 01/10/2026. Conecte-se à internet para sincronizar.* |
| License file copied from another PC | *A concessão de licenças foi emitida para outra estação de trabalho (Hardware ID divergente).* |
| File tampered, corrupted, or wrong format | *A licença local não passou na verificação de segurança. Conecte-se à internet e clique em atualizar no Node.aec Connector.* |
| Computer clock set wrong | *A data da licença local é inválida. Confira a data e hora deste computador e tente novamente.* |

Messages are in Portuguese (PT-BR) only for now.

When `IsLicensed` is `true`, you can also read `gate.LicenseType`, `gate.ProductName` and
`gate.ExpiresAt` (e.g. to show "trial ends on ..." in your UI). `ExpiresAt == null` means
no end date, not "expired".

> [!CAUTION]
> `gate.LicenseKey` is the end user's license key. Never log it, show it, or send it
> anywhere. You don't need it to protect your commands.

### Rules for calling the gate

- **No `try/catch` needed.** `Validate` never throws. Every failure comes back as
  `IsLicensed = false`.
- **Branch only on `IsLicensed`.** Never compare `Message` text to decide what to do,
  because the wording can change between versions.
- **Call it once per command execution**, at the top of `Execute`. It reads and verifies the
  license file on every call. That's fast, but not free.
- **Never call it from `IExternalCommandAvailability`.** Revit evaluates availability
  constantly (on every selection or view change). Leave buttons enabled and check inside
  `Execute`.

### About `Gate.OpenConnector()`

It opens the Connector's *Minha Conta* window. If the Connector is not installed it does
**nothing**: no error and no window. So always show the dialog first.

It creates a WPF window, so it must run on Revit's UI thread: inside `Execute`, or inside an
`ExternalEvent` handler if you call it from a modeless window of your own.

---

## 4. Put your buttons on the `Node.aec` tab

All Node.aec products share a single Ribbon tab called **`Node.aec`**. Create your own panel
inside it, never a separate tab:

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

Rules:
- Use a panel name that is **unique to your product**. `Conector` is used by Node.aec.
  If another add-in already uses your panel name, the code above puts your buttons in
  their panel.
- Don't call tabs or panels `License` or `Licensing`. The Connector removes those because older
  versions used them.
- Don't do anything slow in `OnStartup`, such as network calls or heavy file reads. Revit waits for
  it before it shows the UI.

---

## 5. Package and install your plugin

The gate doesn't care where your plugin is installed. It reads the license from
`%APPDATA%\NodeAec\` and finds the Connector by name inside the Revit process. Use
whichever deployment format you already use:

| | Manifest (`.addin`) | Bundle (`.bundle`) |
|---|---|---|
| Location | `%ProgramData%\Autodesk\Revit\Addins\<year>\` (all users) or `%AppData%\Autodesk\Revit\Addins\<year>\` (current user) | `%ProgramData%\Autodesk\ApplicationPlugins\<Name>.bundle\` (all users) or `%AppData%\Autodesk\ApplicationPlugins\` (current user) |
| Multiple Revit years | One `.addin` per year folder | One bundle; `PackageContents.xml` points each Revit year (`SeriesMin`/`SeriesMax`) to its own DLL folder |
| Works with Node.aec | ✅ | ✅ |

The **Connector itself** installs under `Addins\<year>\NodeAec.Connector\`. That path is
only for the Connector, and your plugin doesn't need to be next to it.

The end user needs **both** installed: the Connector (for sign-in) and your plugin. Your
installer doesn't have to bundle the Connector. If it's missing, your commands show the
*"Nenhuma credencial…"* message, and the user gets the Connector from Node.aec.

### If Revit can't find the package DLLs

Revit normally finds DLLs that sit next to your add-in DLL. If a clean machine shows
`Could not load file or assembly 'NodeAec.Licensing.Lite'` (or `BouncyCastle.Cryptography`)
when a protected command runs, the file is missing from your installer. Check the table in
step 2. If the files are there and the error persists, register a resolver in your
`IExternalApplication`, as the Connector does in its `App.cs`:

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

## 6. Keeping the package up to date

The package contains the Node.aec **public keys** used to verify licenses. Your plugin
only trusts licenses signed with those keys. Node.aec changes the signing key in two
situations:

- **Planned rotation.** Node.aec announces it in advance and first publishes a package that
  trusts **both** the old and the new key. Update the package and republish your plugin
  during that window, and your users notice nothing.
- **Security emergency** (the signing key is compromised). The switch happens immediately,
  and Node.aec notifies partners as soon as possible. Until you publish a plugin built with
  the new package, your users see *"A licença local não passou na verificação de
  segurança…"*. Plan to be able to rebuild and ship quickly.

In both cases the fix on your side is the same: update `NodeAec.Licensing.Lite`, rebuild,
republish.

---

## 7. What the gate protects against, and what it doesn't

The gate stops normal users from running your plugin without a license. It stops
copying the license file to another PC, editing it, forging it, or using it after the
offline period ends. Like any license check that runs on the user's machine, it does **not**
stop a determined attacker who controls that machine:

- **Patching your DLL.** Anyone can decompile a .NET assembly and remove the
  `if (!gate.IsLicensed)` branch. If that matters for your product, obfuscate your
  plugin. That's your responsibility, not the gate's.
- **Other code in Revit.** Any add-in loaded in the same Revit process can tamper with
  other add-ins in memory.
- **Freezing the clock.** Offline, the expiry check trusts the Windows clock. A user who
  keeps the clock between the last sync and the expiry date keeps the license valid.
- **Cloning the machine ID.** A Windows administrator can copy the `MachineGuid` of a
  licensed computer to another one.
- **Reading the license.** The license file is encrypted per Windows user, so any
  program running as that user can read it, license keys included.

---

## 8. Checklist before you ship

- [ ] Builds with 0 errors for every Revit year you support.
- [ ] All your panels are on the `Node.aec` tab, with a panel name unique to your product.
- [ ] Each paid command calls `Gate.Validate("<your-slug>")` first and branches only on `IsLicensed`.
- [ ] The "license required" dialog shows `gate.Message` as-is, with no list of causes around it.
- [ ] `gate.LicenseKey` is never logged or displayed.
- [ ] `Validate` is not called from any `IExternalCommandAvailability`.
- [ ] The installer ships the files from step 2, and no `RevitAPI*.dll` / `AdWindows.dll` / `NodeAec.Connector.dll`.
- [ ] Tested on a clean machine: before signing in (dialog appears), after signing in (command runs), and offline (command still runs).

Questions: open an issue at [github.com/nodeaec/revit-connector/issues](https://github.com/nodeaec/revit-connector/issues)
or contact the Node.aec team.
