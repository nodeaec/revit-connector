# Architecture

How the Node.aec Connector works internally. For integrating a plugin, read the
[Partner Integration Guide](PARTNER_INTEGRATION.md) instead. You don't need this page for that.

---

## Hub & Micro-Gate

Node.aec keeps sign-in, activation and encryption in one **Hub** (the Connector). Each
plugin gets a local **Micro-Gate** (`NodeAecGate`). A partner plugin has no HTTP client,
no activation screens, no individual keys (`NAEC-XXXX-...`), and no encryption of its own.

```
+--------------------------------------------------------------------------+
|                              Autodesk Revit                              |
|                                                                          |
|  [ "Node.aec" Tab ]                                                      |
|                                                                          |
|  +---------------------------+      +----------------------------------+ |
|  |   Node.aec Connector      |      |      Partner Plugins             | |
|  |      (Central Hub)        |      |                                  | |
|  |                           |      |    public Result Execute(...)    | |
|  |  - Browser SSO (loopback) |      |    {                             | |
|  |  - Master Entitlements    |      |      var r = NodeAecGate         | |
|  |    Lease + heartbeat      |      |              .Validate(slug);    | |
|  |  - DPAPI storage          |      |      if (!r.IsLicensed)          | |
|  |  - JWKS / Ed25519         |      |          return Result.Cancelled;| |
|  |  - Tab dedup.             |      |      }                           | |
|  +---------------------------+      +----------------------------------+ |
|   |  writes                                ^  reads, < 1 ms, no network  |
|   v                                        |                             |
|   %APPDATA%\NodeAec\entitlements.lease  (signed, DPAPI-encrypted)        |
|                                                                          |
|   ^ HTTPS                                                                |
|   v                                                                      |
|   https://api.nodeaec.com.br                                             |
+--------------------------------------------------------------------------+
```

What the Hub takes off the partner plugin:

1. **No networking**: the partner knows no endpoints, tokens or response formats.
2. **No activation UI**: login, manual keys and seats live in the Connector's *Minha Conta*
   and *Meus Plugins* windows.
3. **No encryption management**: the Hub writes the lease with `CurrentUser` DPAPI, and the plugin only reads it.
4. **One sign-in**: every product on the account is synced together.
5. **Local validation in < 1 ms**, with 30 days of offline tolerance.
6. **One Ribbon tab**: everything lives on `Node.aec`.

---

## License and heartbeat flow

| # | When | What happens |
|---|---|---|
| 1 | User clicks **Entrar com minha conta** | The Connector listens on a free `127.0.0.1` port and opens `https://nodeaec.com.br/auth/desktop?port=…&state=…` in the default browser (120 s timeout, anti-CSRF `state`, RFC 8252). |
| 2 | Browser login completes | The portal redirects to `http://127.0.0.1:<port>/callback?token=…&state=…`; the listener validates `state` and shows *Login Concluído*. |
| 3 | Sync | `POST /account/entitlements/lease` returns the Ed25519-signed **Master Entitlements Lease**. |
| 4 | Response received | `GET /license/jwks` refreshes the public-key cache, then the lease is written to `%APPDATA%\NodeAec\entitlements.lease` with DPAPI. A failed write shows an error, and the lease is never written in plaintext. |
| 5 | Every Revit launch | Background heartbeat (`Task.Run`): `POST /license/validate` renews the lease. If the network fails, nothing is shown to the user and the failure goes to `connector.log`. |
| 6 | Partner plugin runs | `NodeAecGate.Validate(slug)` checks signature → `iss` → `scope` → `aud` → `iat` → `mid` → `exp` → `slug`. **No network.** |
| 7 | No internet | The local lease stays valid until its `exp` (30 days by default). Each online launch extends it. |

HTTP contract (endpoints, payloads, claims, error codes): [licensing-api.md](licensing-api.md).

---

## Security

- **Signature first**: the lease's Ed25519 (RFC 8032) signature is checked before any claim is trusted.
- **Compiled trust anchor**: the production public key is compiled in
  (`ConnectorConfig.DefaultLicensePublicKeySpkiBase64`) and is the **only** key accepted.
  The JWKS cache (`%APPDATA%\NodeAec\license-jwks.json`) is used to discover keys (`kid`)
  and signal rotation, but it is never trusted to verify a lease. `NODEAEC_LICENSE_PUBLIC_KEY_SPKI`
  can replace the anchor **only in test builds** (`NODEAEC_ANCHOR_OVERRIDE`). Production
  ignores it, because an environment variable is user-controlled. If no usable anchor is
  available, the gate denies access.
- **DPAPI storage**: `entitlements.lease` and `session.json` are encrypted with
  `DataProtectionScope.CurrentUser`. A DPAPI failure never falls back to plaintext.
- **Machine binding**: the lease carries a SHA-256 of the Windows `MachineGuid`. A copied file
  fails on another computer.
- **Offline / air-gapped**: manual key entry (`NAEC-XXXX-...`). Importing `.lease` files is
  planned but not available yet.
- **Diagnostics**: API errors map to stable codes. A sanitized log goes to
  `%APPDATA%\NodeAec\connector.log` (512 KB rotation, no tokens).

---

## Ribbon

The Connector owns the canonical `Node.aec` tab and its `Conector` panel. On startup and on
`ApplicationInitialized` it:

- merges duplicate `Node.aec` tabs (keeping one panel per name) via `Autodesk.Windows.ComponentManager`;
- removes legacy elements: `License` / `Licensing` tabs and the old `Conectar Conta` button.

---

## Repository layout

```text
revit-connector/
├── AGENTS.md                          # Rules for AI agents in this repository
├── Directory.Build.props              # Per-Revit-year build matrix (2023–2027)
├── NodeAec.Connector.sln
├── docs/                              # Manuals, integration guide, API contract
├── scripts/release.ps1                # Build, package, and local-install script
├── src/NodeAec.Connector/
│   ├── Auth/                          # Browser SSO (loopback, RFC 8252)
│   ├── Client/                        # API client (Master Entitlements Lease)
│   ├── Gate/                          # NodeAecGate — what partner plugins call
│   ├── Storage/                       # DPAPI persistence in %APPDATA%\NodeAec
│   ├── UI/                            # WPF windows
│   └── App.cs                         # IExternalApplication (Ribbon and dedup)
└── tests/NodeAec.Connector.Tests/     # Unit tests (net8.0, no Revit needed)
```

---

## Build matrix and packaging

Each Revit year is built against its own runtime: `net48` (2023/2024), `net8.0-windows`
(2025/2026), `net10.0-windows` (2027). `release.ps1 -RevitYear <group>` produces, per
compatibility group (`2023-2024`, `2025-2026`, `2027`):

- `release/NodeAec.Connector-<version>-R<group>.zip`
- `release/NodeAec.Connector-<version>-R<group>-Setup.exe` (needs [Inno Setup 6](https://jrsoftware.org/isdl.php))

The installer lists the group's Revit years installed on the machine (or *all installed
versions*). The payload is compiled for the group's base year, and each year gets its own `.addin`
under `%ProgramData%\Autodesk\Revit\Addins\<year>\`. Silent installs target every installed
year (`/RevitYear=<year>` picks one). Uninstall asks which year to remove when more than one is
present.
