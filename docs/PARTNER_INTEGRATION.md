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
  <!-- Copies NuGet DLLs (BouncyCastle, etc.) next to your add-in DLL -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
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

<!-- Revit 2025/2026 (net8.0-windows) only -->
<ItemGroup Condition="'$(TargetFramework)' == 'net8.0-windows'">
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
</ItemGroup>

<!-- Revit 2027 (net10.0-windows): nothing extra -->
```

**What must go in your installer, next to your plugin DLL:**

| Revit | Ship these DLLs |
|---|---|
| 2023 / 2024 | `BouncyCastle.Cryptography.dll`, `System.Security.Cryptography.ProtectedData.dll`, `System.Text.Json.dll` and its dependencies |
| 2025 / 2026 / 2027 | `BouncyCastle.Cryptography.dll` only. **Do not** ship `System.Security.Cryptography.ProtectedData.dll` — Revit already provides it. |

Never ship `RevitAPI.dll`, `RevitAPIUI.dll` or `AdWindows.dll`.

---

## 4. Put your buttons on the `Node.aec` tab

All Node.aec products share a single Ribbon tab called **`Node.aec`**. Create your own panel
inside it — never a separate tab:

```csharp
public Result OnStartup(UIControlledApplication application)
{
    const string tabName = "Node.aec";
    const string panelName = "Minhas Ferramentas"; // your product's panel

    // The tab may already exist (created by the Connector or another partner).
    try { application.CreateRibbonTab(tabName); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }

    var panel = application.GetRibbonPanels(tabName).FirstOrDefault(p => p.Name == panelName)
                ?? application.CreateRibbonPanel(tabName, panelName);

    panel.AddItem(new PushButtonData(
        "MeuPlugin_Comando", "Executar\nComando",
        typeof(App).Assembly.Location, "MeuPlugin.Commands.MeuComando"));

    return Result.Succeeded;
}
```

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

When `IsLicensed` is `true`, you can also read `check.LicenseType`, `check.ProductName` and
`check.ExpiresAt` (e.g. to show "trial ends on ..." in your UI).

### About `OpenConnector()`

It opens the Connector's *Minha Conta* window. If the Connector is not installed it does
**nothing**: no error and no window. So always show the dialog first, and never rely on
`OpenConnector()` alone to tell the user something is wrong.

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

## 7. Checklist before you ship

- [ ] Builds with 0 errors for every Revit year you support.
- [ ] All your panels are on the `Node.aec` tab, with a panel name unique to your product.
- [ ] Each paid command calls `NodeAecGate.Validate("<your-slug>")` first.
- [ ] The "license required" dialog shows `check.Message` as-is.
- [ ] Revit 2023/2024 payload contains `System.Security.Cryptography.ProtectedData.dll`; Revit 2025+ payload does **not**.
- [ ] No `RevitAPI*.dll` / `AdWindows.dll` in your installer.
- [ ] Tested on a clean machine: before signing in (dialog appears), after signing in (command runs), and offline (command still runs).

Questions: open an issue at [github.com/nodeaec/revit-connector/issues](https://github.com/nodeaec/revit-connector/issues)
or contact the Node.aec team.
