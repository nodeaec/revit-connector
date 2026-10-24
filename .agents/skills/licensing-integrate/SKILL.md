---
name: licensing-integrate
description: Integrate Node.aec licensing into an existing Autodesk Revit plugin (.NET 8 for Revit 2025/2026+ or .NET Framework 4.8 for Revit 2023-2024). Trigger whenever asked to "integrate node.aec licensing", "add license check", "protect revit command", "configure licensing client", "setup node.aec licensing", "activate license in plugin", or when modifying licensing logic in a Revit add-in.
---

# Licensing Integrate (Hub & Micro-Gate Architecture)

This skill guides the end-to-end integration of Node.aec licensing into an Autodesk Revit add-in using the **Hub & Micro-Gate (NodeAecGate)** pattern. It enforces a strict pre-implementation interview (Grilling Phase), fixed architectural invariants, and drop-in code recipes.

---

## 🛑 Phase 0: Mandatory Grilling Protocol

> [!IMPORTANT]
> **NEVER generate code or alter the user's project before executing this interview.**
> Present each question with context and your explicit recommended default, and wait for the user's alignment.

### Non-Negotiable Invariants (Do NOT Ask the User):
1. **Hub & Micro-Gate Architecture**: Partner plugins DO NOT implement HTTP clients, cloud auth, or license manager UIs. All account SSO, seat management, lease sync, and manual key activations are handled by the central **Node.aec Connector**.
2. **Instant Local Validation (< 1ms, Zero Network)**: Plugins validate authorization locally using `NodeAecGate.Validate(slug)`. Never perform HTTP requests on Revit commands or application startup.
3. **Ribbon Tab**: The plugin MUST ALWAYS place its panels and buttons in the canonical **`Node.aec`** tab (`TabName = "Node.aec"`). If the plugin currently has its own ribbon tab or scattered commands, migrate all panels and commands to the `Node.aec` tab. Never create a separate tab.
4. **Offline Tolerance**: Leases stored in `%APPDATA%\NodeAec\entitlements.lease` allow up to 30 days of seamless offline operation.

---

### Grilling Questions

#### 1. Revit Version & .NET Target Framework
- **Question**: *"Which Autodesk Revit versions does this plugin target?"*
- **Context**: Revit 2025 and 2026+ require **.NET 8** (`net8.0-windows`). Revit 2023–2024 require **.NET Framework 4.8** (`net48`).
- **Options**:
  - `A` (Recommended for modern plugins): .NET 8 exclusive (`<TargetFramework>net8.0-windows</TargetFramework>`).
  - `B`: .NET Framework 4.8 exclusive (`<TargetFramework>net48</TargetFramework>`).
  - `C`: Multi-targeting (`<TargetFrameworks>net8.0-windows;net48</TargetFrameworks>`).

#### 2. Product Slug on Node.aec
- **Question**: *"What is the registered product slug on Node.aec (`https://nodeaec.com.br/products/{slug}`)? If not yet registered, what slug should we use?"*
- **Recommendation**: Use the exact kebab-case slug (e.g. `"revit-automator"`, `"parametric-doors"`).

#### 3. Ribbon Panel Name
- **Question**: *"What panel name should be used for your tools under the canonical 'Node.aec' Ribbon tab?"*
- **Recommendation**: Use a concise thematic name (e.g. `"Automação"`, `"Modelagem"`, `"Ferramentas"`).

#### 4. Commercial Command Gating Policy
- **Question**: *"How should commercial commands behave when no valid license is active on this workstation?"*
- **Options**:
  - `A` (Recommended): **Hard Gate** — Show an informative `TaskDialog` with a direct command link to "Abrir Node.aec Connector..." and cancel command execution (`Result.Cancelled`).
  - `B`: **Trial / Grace Mode** — Allow execution with limitations (e.g. element count limit, watermark, or remaining trial days).

---

## 📋 Integration Recipes

> [!IMPORTANT]
> The human-facing source of truth is [`docs/PARTNER_INTEGRATION.md`](../../../docs/PARTNER_INTEGRATION.md).
> Keep these recipes consistent with it.

### Recipe 1: Copy the gate source files

Copy from `github.com/nodeaec/revit-connector` (`src/NodeAec.Connector/`) into the plugin,
**keeping folders and namespaces unchanged** (`NodeAec.Connector.*`). This is the full
compile closure of `NodeAecGate`. Anything less fails to build:

1. `Gate/NodeAecGate.cs`
2. `Hardware/HardwareId.cs`
3. `Cryptography/LeaseSignatureVerifier.cs`
4. `Storage/LeaseStorage.cs`, `Storage/SigningKeyStore.cs`
5. `Models/*.cs` (all four)
6. `Config/ConnectorConfig.cs` (compiled-in Ed25519 public key)
7. `Diagnostics/ConnectorLog.cs`
8. `Compatibility/NetFrameworkPolyfills.cs`: **`net48` only** (records need `IsExternalInit`)

Do **not** copy `Auth/`, `Client/`, `UI/` or `App.cs`.

This closure plus Recipe 2 was compiled standalone for `net48`, `net8.0-windows` and
`net10.0-windows` with 0 errors / 0 warnings. If a clean machine raises
`FileNotFoundException` for BouncyCastle or System.Text.Json, add the add-in-folder
resolver shown in `docs/PARTNER_INTEGRATION.md` (same as the Connector's `App.cs`).

### Recipe 2: Project file (`.csproj`)

The copied files use file-scoped namespaces, records and nullable annotations, so
`LangVersion latest` is mandatory on `net48` (whose default is C# 7.3).

```xml
<PropertyGroup>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
  <!-- net8/net10: ProtectedData comes from Microsoft.WindowsDesktop.App. Without UseWPF
       (or an explicit FrameworkReference to it), net10.0-windows does not compile. -->
  <UseWPF>true</UseWPF>
  <!-- Copies NuGet dependency DLLs to the add-in folder, never RevitAPI*.dll -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  <!-- net48 reference assemblies are nullable-oblivious: false positives only -->
  <NoWarn Condition="'$(TargetFramework)' == 'net48'">$(NoWarn);CS8602;CS8603;CS8604</NoWarn>
</PropertyGroup>

<ItemGroup>
  <!-- Ed25519 lease signature verification -->
  <PackageReference Include="BouncyCastle.Cryptography" Version="2.7.0" />
</ItemGroup>

<ItemGroup Condition="'$(TargetFramework)' == 'net48'">
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="4.7.0" />
  <PackageReference Include="System.Text.Json" Version="8.0.5" />
  <Reference Include="System.Net.Http" />
</ItemGroup>

<ItemGroup Condition="'$(TargetFramework)' == 'net8.0-windows'">
  <!-- ExcludeAssets="runtime": compile against it, never copy it to the payload -->
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" ExcludeAssets="runtime" />
</ItemGroup>
<!-- net10.0-windows: nothing extra -->
```

> [!NOTE]
> `System.Security.Cryptography.ProtectedData.dll` is a staged payload item **only for
> `net48`** (Revit 2023/2024), where the NuGet package is its only source. On
> `net8.0-windows`/`net10.0-windows` (Revit 2025+) the assembly ships inside the host's
> `Microsoft.WindowsDesktop.App` runtime (verified in the 8.0.31 and 10.0.12 packs), so it
> must **not** be copied into the add-in folder. Encrypting with `ProtectedData` is
> unaffected on every target.

### Recipe 3: Application Lifecycle (`App.cs`) — Ribbon on the `Node.aec` tab

```csharp
using System;
using System.Linq;
using Autodesk.Revit.UI;

public class App : IExternalApplication
{
    public const string TabName = "Node.aec";
    public const string PanelName = "Minhas Ferramentas";

    public Result OnStartup(UIControlledApplication application)
    {
        // 1. Invariant: Always use the canonical "Node.aec" tab
        try { application.CreateRibbonTab(TabName); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { /* tab already exists: expected */ }

        // Defensive acquisition: prevent ArgumentException if panel already exists
        var panel = application.GetRibbonPanels(TabName)
            .FirstOrDefault(p => string.Equals(p.Name, PanelName, StringComparison.OrdinalIgnoreCase))
            ?? application.CreateRibbonPanel(TabName, PanelName);

        var btn = new PushButtonData(
            "MeuPlugin_Comando",
            "Executar\nComando",
            typeof(App).Assembly.Location,
            "SeuNamespace.Commands.MeuComandoComercial"
        )
        {
            ToolTip = "Executa a ferramenta comercial protegida via NodeAecGate."
        };
        panel.AddItem(btn);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
```

Panel names must be unique to the product (`Conector` is reserved). Never use `License` /
`Licensing` as tab or panel names, because the Connector removes them as legacy elements.

### Recipe 4: Protecting an `IExternalCommand`

`check.Message` is a complete, end-user-facing explanation of the **one** specific failure
reason, already including the next step (e.g. "Abra o Node.aec Connector…") where it
applies. Show it as-is: never wrap it in a list of possible causes or append a second
"open the Connector" sentence.

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.Gate;

[Transaction(TransactionMode.Manual)]
public class MeuComandoComercial : IExternalCommand
{
    private const string ProductSlug = "meu-produto-slug";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // 1. Instant local check (< 1ms, zero network)
        var check = NodeAecGate.Validate(ProductSlug);
        if (!check.IsLicensed)
        {
            var dialog = new TaskDialog("Node.aec")
            {
                MainInstruction = "Licença necessária",
                MainContent = check.Message,
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir Node.aec Connector");

            // OpenConnector() is a silent no-op when the Connector is not installed,
            // which is why the dialog always comes first.
            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                NodeAecGate.OpenConnector();
            }

            return Result.Cancelled;
        }

        // --- 2. Execute commercial logic ---
        return Result.Succeeded;
    }
}
```

Gate call rules:
- `Validate` never throws, so don't wrap it in `try/catch`.
- Call it once per `Execute`, never from `IExternalCommandAvailability` (it reads and verifies the lease on every call).
- `OpenConnector()` creates a WPF window: Revit UI thread only (`Execute` or an `ExternalEvent` handler).
- `check.LicenseKey` is the end user's secret: never log, display, or transmit it.

### Recipe 5: Deployment format

`NodeAecGate` is location-independent: it reads `%APPDATA%\NodeAec\` and finds the
Connector by assembly name in the Revit process. The partner keeps whatever format it
already uses: a `.addin` manifest under `Addins\<year>\` or a `.bundle` under
`ApplicationPlugins\`. The `Addins\<year>\NodeAec.Connector\` path in this repository
applies only to the Connector itself.

---

## ✅ Validation Checklist

- [ ] Project builds with **0 errors**.
- [ ] `System.Security.Cryptography.ProtectedData.dll` is present in the add-in output folder **only when targeting `net48`** (Revit 2023/2024); on `net8.0-windows`/`net10.0-windows` it is provided by `Microsoft.WindowsDesktop.App` and must be absent.
- [ ] No `RevitAPI*.dll` assemblies copied into output.
- [ ] No HTTP clients or login forms implemented inside the partner plugin.
- [ ] All plugin commands and panels are consolidated under the **`Node.aec`** tab.
- [ ] Executing a protected command without license shows `check.Message` as-is in the TaskDialog, with the link to the **Node.aec Connector**.
- [ ] Panel name is unique to the product and lives on the `Node.aec` tab.
- [ ] `Validate` is never called from `IExternalCommandAvailability`; `LicenseKey` is never logged or displayed.
- [ ] The commit hash of the copied gate files is recorded in the partner repository (needed to update after a signing-key rotation).
- [ ] Offline operation functions properly with the local master lease.
