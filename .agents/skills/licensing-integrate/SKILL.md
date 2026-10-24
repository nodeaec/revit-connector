---
name: licensing-integrate
description: Integrate Node.aec licensing into an existing Autodesk Revit plugin (.NET 8 for Revit 2025/2026+ or .NET Framework 4.8 for Revit 2023-2024). Trigger whenever asked to "integrate node.aec licensing", "add license check", "protect revit command", "configure licensing client", "setup node.aec licensing", "activate license in plugin", or when modifying licensing logic in a Revit add-in.
---

# Licensing Integrate (Hub & Micro-Gate Architecture)

This skill guides the end-to-end integration of Node.aec licensing into an Autodesk Revit add-in using the **Hub & Micro-Gate (`NodeAec.Licensing.Lite`)** pattern. It enforces a strict pre-implementation interview (Grilling Phase), fixed architectural invariants, and drop-in code recipes.

---

## 🛑 Phase 0: Mandatory Grilling Protocol

> [!IMPORTANT]
> **NEVER generate code or alter the user's project before executing this interview.**
> Present each question with context and your explicit recommended default, and wait for the user's alignment.

### Non-Negotiable Invariants (Do NOT Ask the User):
1. **Hub & Micro-Gate Architecture**: Partner plugins DO NOT implement HTTP clients, cloud auth, or license manager UIs. All account SSO, seat management, lease sync, and manual key activations are handled by the central **Node.aec Connector**.
2. **Instant Local Validation (Zero Network)**: Plugins validate authorization locally using `Gate.Validate(slug)` from the `NodeAec.Licensing.Lite` NuGet package. Never copy gate source files or `NodeAec.Connector.dll` into a partner plugin. Never perform HTTP requests on Revit commands or application startup.
3. **Ribbon Tab**: The plugin MUST ALWAYS place its panels and buttons in the canonical **`Node.aec`** tab (`TabName = "Node.aec"`). If the plugin currently has its own ribbon tab or scattered commands, migrate all panels and commands to the `Node.aec` tab. Never create a separate tab.
4. **Offline Tolerance**: Leases stored in `%APPDATA%\NodeAec\entitlements.lease` allow up to 30 days of seamless offline operation.

---

### Grilling Questions

#### 1. Revit Version & .NET Target Framework
- **Question**: *"Which Autodesk Revit versions does this plugin target?"*
- **Context**: Revit 2025/2026 require **.NET 8** (`net8.0-windows`), Revit 2027 requires **.NET 10** (`net10.0-windows`), and Revit 2023–2024 require **.NET Framework 4.8** (`net48`).
- **Options**:
  - `A` (Recommended for modern plugins): .NET 8 exclusive (`<TargetFramework>net8.0-windows</TargetFramework>`).
  - `B`: .NET Framework 4.8 exclusive (`<TargetFramework>net48</TargetFramework>`).
  - `C`: Multi-targeting (`<TargetFrameworks>net48;net8.0-windows;net10.0-windows</TargetFrameworks>`).

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
> The human-facing source of truth is [`docs/PARTNER_INTEGRATION.md`](../../../docs/PARTNER_INTEGRATION.md),
> and the complete reference add-in is `revit-sample-plugin`. Keep these recipes consistent with both.

### Recipe 1: Package and project file (`.csproj`)

```powershell
dotnet add package NodeAec.Licensing.Lite   # latest version on nuget.org
```

```xml
<PropertyGroup>
  <!-- Copies Lite + BouncyCastle (+ net48 System.Text.Json/ProtectedData) next to the add-in DLL -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  <!-- On net8/net10 keeps ProtectedData.dll out of the payload (Revit provides it) -->
  <UseWPF>true</UseWPF>
</PropertyGroup>
```

Verified with `NodeAec.Licensing.Lite` 0.1.0 for `net48`, `net8.0-windows` and
`net10.0-windows`: 0 errors, 0 warnings, no extra package references needed. Without
`UseWPF`, `System.Security.Cryptography.ProtectedData.dll` lands in the 2025+ payload.

Payload per Revit year: 2023/2024 → `NodeAec.Licensing.Lite.dll`, `BouncyCastle.Cryptography.dll`,
`System.Security.Cryptography.ProtectedData.dll`, `System.Text.Json.dll` + dependencies;
2025+ → `NodeAec.Licensing.Lite.dll`, `BouncyCastle.Cryptography.dll`. Never `RevitAPI*.dll`,
`AdWindows.dll` or `NodeAec.Connector.dll`.

### Recipe 2: Application Lifecycle (`App.cs`) — Ribbon on the `Node.aec` tab

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
            "SeuPlugin.Commands.MeuComandoComercial"
        )
        {
            ToolTip = "Executa a ferramenta comercial protegida pelo Node.aec."
        };
        panel.AddItem(btn);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
```

Panel names must be unique to the product (`Conector` is reserved). Never use `License` /
`Licensing` as tab or panel names, because the Connector removes them as legacy elements.

### Recipe 3: Protecting an `IExternalCommand`

`gate.Message` is a complete, end-user-facing explanation of the **one** specific failure
reason, already including the next step where it applies. Show it as-is: never wrap it in
a list of possible causes or append a second "open the Connector" sentence.

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Licensing;

namespace SeuPlugin.Commands;

[Transaction(TransactionMode.Manual)]
public class MeuComandoComercial : IExternalCommand
{
    private const string ProductSlug = "meu-produto-slug";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // 1. Instant local check (zero network, never throws)
        var gate = Gate.Validate(ProductSlug);
        if (!gate.IsLicensed)
        {
            var dialog = new TaskDialog("Node.aec")
            {
                MainInstruction = "Licença necessária",
                MainContent = gate.Message,
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir Node.aec Connector");

            // OpenConnector() is a silent no-op when the Connector is not installed,
            // which is why the dialog always comes first.
            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                Gate.OpenConnector();
            }

            return Result.Cancelled;
        }

        // --- 2. Execute commercial logic ---
        return Result.Succeeded;
    }
}
```

Gate call rules:
- `Validate` never throws, so don't wrap it in `try/catch`. Branch only on `IsLicensed`. Never match `Message` text.
- Call it once per `Execute`, never from `IExternalCommandAvailability` (it reads and verifies the lease on every call).
- `Gate.OpenConnector()` creates a WPF window: Revit UI thread only (`Execute` or an `ExternalEvent` handler).
- `gate.LicenseKey` is the end user's secret: never log, display, or transmit it.

### Recipe 4: Deployment format

The gate is location-independent: it reads `%APPDATA%\NodeAec\` and finds the
Connector by assembly name in the Revit process. The partner keeps whatever format it
already uses: a `.addin` manifest under `Addins\<year>\` or a `.bundle` under
`ApplicationPlugins\`. The `Addins\<year>\NodeAec.Connector\` path in this repository
applies only to the Connector itself.

### Recipe 5: Updates and key rotation

Lite verifies against compiled public keys. Planned rotations ship a Lite version that
trusts old + new keys first (announced in advance); a security emergency switches
immediately. Either way the partner updates `NodeAec.Licensing.Lite`, rebuilds and
republishes. Advise the partner to keep that path fast.

---

## ✅ Validation Checklist

- [ ] Project builds with **0 errors** for every targeted Revit year.
- [ ] `NodeAec.Licensing.Lite` comes from a `PackageReference`; no copied gate sources, no `NodeAec.Connector.dll`.
- [ ] Payload matches Recipe 1 (ProtectedData only on `net48`); no `RevitAPI*.dll` / `AdWindows.dll`.
- [ ] No HTTP clients or login forms implemented inside the partner plugin.
- [ ] All panels are on the **`Node.aec`** tab, with a panel name unique to the product.
- [ ] Each protected command calls `Gate.Validate(slug)` first and branches only on `IsLicensed`.
- [ ] Without a license the TaskDialog shows `gate.Message` as-is (no list of causes) with the link to the **Node.aec Connector**.
- [ ] `Validate` is never called from `IExternalCommandAvailability`; `LicenseKey` is never logged or displayed.
- [ ] Offline operation works with the local master lease.
