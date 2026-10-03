# Node.aec Connector — Autodesk Revit Add-in

Central desktop governance, license management, and unified Ribbon add-in for **Autodesk Revit 2023–2027**, with one standalone installer per year (build matrix).

The **Node.aec Connector** acts as the Hub in the **Hub & Micro-Gate** model: the end user signs in once in the browser (Browser SSO with local loopback, RFC 8252) and has all of their plugins, templates, and families licensed and automatically synced on the workstation, with up to 30 days of offline tolerance.

Official repository: [github.com/nodeaec/revit-connector](https://github.com/nodeaec/revit-connector) · Issues: use the [issue tracker](https://github.com/nodeaec/revit-connector/issues) to report problems with step-by-step reproduction.

📖 **Documentation**: [User Manual](docs/USER_MANUAL.md) · [Licensing API Contract](docs/licensing-api.md)

---

## 🚀 Key Features

- **Canonical `Node.aec` Tab**: Registers and manages the official `Conector` panel on the Revit Ribbon with quick-access buttons and automatic tab deduplication via `AdWindows`.
- **Browser SSO (Local Loopback OAuth 2.0 — RFC 8252)**: Modern, secure authentication with Google login and 2FA support, no password typing inside Revit.
- **Master Entitlements Lease**: Fetches and renews consolidated grants for multiple products, with Ed25519 (RFC 8032) signature verification **before** trusting any claim.
- **Compiled signature anchor (pin)**: The production Ed25519 public key is compiled into the add-in (`ConnectorConfig.DefaultLicensePublicKeySpkiBase64`) and is the **only** key accepted for verifying leases. The JWKS (`GET /license/jwks`, cached at `%APPDATA%\NodeAec\license-jwks.json`) serves `kid` discovery and rotation signaling, never as a trust source. Operations may override the anchor via `NODEAEC_LICENSE_PUBLIC_KEY_SPKI`; without a usable anchor the gate fails closed.
- **Secure DPAPI Storage**: The `%APPDATA%\NodeAec\entitlements.lease` file is encrypted with `DataProtectionScope.CurrentUser`; a DPAPI failure on Windows never degrades to plaintext.
- **Offline & Air-Gapped Mode**: Manual key entry (`NAEC-XXXX-...`). Importing `.lease` files is **deferred to a future iteration** and the corresponding link has been **removed from the UI** (the export/exchange format is not a stable contract yet).
- **`NodeAecGate` Micro-SDK**: Canonical class for partner plugins to validate execution permission locally — no network requests on the critical path, in a few milliseconds.
- **Local Diagnostics**: API errors mapped to stable codes and a sanitized log at `%APPDATA%\NodeAec\connector.log` (512 KB rotation, no tokens).

---

## 🏛️ Architecture: Hub & Micro-Gate

Instead of each partner plugin implementing its own HTTP client, showing activation
screens, asking for individual keys (`NAEC-XXXX-...`), and managing machine encryption,
Node.aec concentrates everything in a **Hub** and hands plugins a local **Micro-Gate**:

```
+--------------------------------------------------------------------------+
|                              Autodesk Revit                              |
|                                                                          |
|  [ "Node.aec" Tab ]                                                      |
|                                                                          |
|  +---------------------------+      +----------------------------------+ |
|  |   Node.aec Connector      |      |      Partner Plugins             | |
|  |      (Central Hub)         |      |   (Revit Automator, Portas, ...) | |
|  |                           |      |                                  | |
|  |  - Browser SSO (loopback) |      |    public Result Execute(...)    | |
|  |  - Master Entitlements    |      |    {                             | |
|  |    Lease + heartbeat      |      |      var r = NodeAecGate         | |
|  |  - DPAPI storage          |      |              .Validate(slug);    | |
|  |  - JWKS / Ed25519         |      |      if (!r.IsLicensed)          | |
|  |  - Tab dedup.             |      |          return Result.Cancelled;| |
|  +---------------------------+      |      }                           | |
|                                     +----------------------------------+ |
|   ^                                 | local plugin read, < 1 ms          |
|   | signed lease, in DPAPI        | no network call at all             |
|   | at %APPDATA%\NodeAec\           |                                    |
|   | entitlements.lease              |                                    |
|                                                                          |
|   ^                                 |                                    |
|   | HTTPS                           | opens the default browser          |
|   v                                 v                                    |
|   https://api.nodeaec.com.br        https://nodeaec.com.br               |
+--------------------------------------------------------------------------+
```

**What the Hub absorbs for the partner plugin**

1. **No networking infrastructure in the plugin** — the partner writes no HTTP client and
   knows no endpoints, session tokens, or response formats.
2. **No proprietary activation UI** — login, manual keys, and seat management live in the
   *Minha Conta* and *Meus Plugins* windows.
3. **No encryption management** — the Hub writes the lease with `CurrentUser` DPAPI and fails
   closed; the plugin only reads.
4. **A single authentication** — the user signs in once and every product on the account is
   synced together.
5. **Local validation in < 1 ms** — `NodeAecGate` is pure CPU, with no network I/O on the
   critical path, and 30 days of offline tolerance.
6. **Unified Ribbon** — everything happens on the canonical `Node.aec` tab, with no fragmented tabs.

> HTTP contract consumed by the Hub (endpoints, payloads, claims, and error codes):
> [docs/licensing-api.md](docs/licensing-api.md).

---

## 🔄 License and Heartbeat Flow

| # | When | What happens |
|---|---|---|
| 1 | User clicks **Entrar com minha conta** | The Connector picks a free ephemeral port, listens on `127.0.0.1`, and opens `https://nodeaec.com.br/auth/desktop?port=…&state=…` in the default browser (120 s timeout, anti-CSRF `state`). |
| 2 | Browser login completes | The portal redirects to `http://127.0.0.1:<port>/callback?token=…&state=…`; the listener validates `state` and returns the *Login Concluído* page. |
| 3 | Window triggers sync | `POST /account/entitlements/lease` returns the Ed25519-signed **Master Entitlements Lease**. |
| 4 | Response received | `GET /license/jwks` refreshes the informative public-key cache (verification uses the compiled-in anchor, not the cache), then the lease is written to `%APPDATA%\NodeAec\entitlements.lease` with DPAPI. A failed write ⇒ user-facing error, never plaintext. |
| 5 | Revit launch (always) | Heartbeat on `Task.Run`: `POST /license/validate` renews the lease; a network failure is silent and stays in `connector.log`. |
| 6 | Partner plugin runs | `NodeAecGate.Validate(slug)` checks signature → `iss` → `scope` → `iat` → `mid` → `exp` → `slug` and grants or blocks — **no network**. |
| 7 | No internet | The local lease is valid until the API-issued `exp` (30-day default); on every Revit launch validity is opportunistically extended. |

---

## 📁 Repository Layout

```text
revit-connector/
├── AGENTS.md                          # Guidelines and rules for AI agents in this repository
├── Directory.Build.props              # Per-Revit-year build matrix (2023–2027)
├── README.md                          # This document (full Connector documentation)
├── NodeAec.Connector.sln              # Solution (.NET/Revit 2023–2027 matrix)
├── docs/                              # User manual and licensing API contract
├── scripts/
│   └── release.ps1                    # Build, packaging, and local-deploy script
├── src/NodeAec.Connector/
│   ├── Auth/                          # DesktopAuthService (Browser SSO Loopback RFC 8252)
│   ├── Client/                        # ConnectorApiClient (Master Entitlements Lease)
│   ├── Gate/                          # NodeAecGate (local validation Micro-SDK < 1ms)
│   ├── Storage/                       # LeaseStorage (DPAPI persistence %APPDATA%\NodeAec)
│   ├── UI/                            # ConnectorWindow (modern WPF interface)
│   └── App.cs                         # IExternalApplication (Node.aec Ribbon and dedup)
└── tests/NodeAec.Connector.Tests/     # Unit tests (net8.0, CI-safe)
```

---

## 🛠️ Environment and Prerequisites

To build and contribute to the add-in:

- **Operating System**: Windows 10 or 11 (64-bit)
- **Autodesk Revit**: 2023 through 2027 installed at the default path (`C:\Program Files\Autodesk\Revit <year>`) — building and packaging are done one year at a time (`-RevitYear`)
- **.NET SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Shell**: PowerShell 5.1 or PowerShell 7+

---

## 💻 How to Build and Test

```powershell
# Build the solution
dotnet build NodeAec.Connector.sln -c Release

# Run the unit tests (headless, no Revit)
dotnet test tests\NodeAec.Connector.Tests\NodeAec.Connector.Tests.csproj

# Package the .zip and the installer for ONE Revit year group and install locally
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.2 -RevitYear 2025-2026 -Install
```

Each script run produces artifacts for **one Revit compatibility group** (`-RevitYear`, default `2025-2026`; groups `2023-2024`, `2025-2026`, and `2027`, with a lone year resolving to its group): the `.zip` `release/NodeAec.Connector-<version>-R<group>.zip` and, when [Inno Setup 6](https://jrsoftware.org/isdl.php) is installed, the installer `release/NodeAec.Connector-<version>-R<group>-Setup.exe`. In the wizard, the user picks the Revit version from a list of the group years installed on the machine (or keeps **all installed versions**); the payload is compiled with the group's base year (`2023`, `2025`, or `2027`) and each year gets its own manifest. Silent installs target every installed year of the group (`/RevitYear=<year>` picks one). Uninstall asks which version to remove (or all of them) when more than one is present, and each group has its own Programs entry. Repeat with `-RevitYear 2023-2024` and `-RevitYear 2027` to produce all three installers; without Inno Setup only the `.zip` is produced.

---

## 🔌 Integrating Partner Plugins with `NodeAecGate`

In your plugin commands (`IExternalCommand`):

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.Gate;

[Transaction(TransactionMode.Manual)]
public class MeuComandoRevit : IExternalCommand
{
    private const string ProductSlug = "meu-plugin";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // Local, instant validation (< 1ms, zero network)
        var check = NodeAecGate.Validate(ProductSlug);
        if (!check.IsLicensed)
        {
            TaskDialog.Show("Node.aec — Licença Necessária",
                $"O produto '{ProductSlug}' não possui licença ativa nesta estação.\n\n" +
                $"Motivo: {check.Message}\n\n" +
                "Abra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.");

            NodeAecGate.OpenConnector();
            return Result.Cancelled;
        }

        // Normal feature execution
        TaskDialog.Show("Sucesso", $"Executando com licença {check.LicenseType}.");
        return Result.Succeeded;
    }
}
```

---

## 🤝 How to Contribute

AEC community contributions are very welcome!
1. Create a branch from `main` (`feature/your-improvement`).
2. Follow the architecture and code guidelines in [`AGENTS.md`](AGENTS.md).
3. Make sure the build runs with **0 errors**.
4. Open a Pull Request describing the changes and their purpose for the ecosystem.

---

## 🌐 Node.aec Ecosystem

- **Official Portal**: [nodeaec.com.br](https://nodeaec.com.br)
- **Tools Catalog**: [nodeaec.com.br/products](https://nodeaec.com.br/products)
- **Developer Area**: [nodeaec.com.br/workspace](https://nodeaec.com.br/workspace)
- **Support & Community**: Open an issue in this repository or contact the Node.aec team.
