# Architecture

How the Node.aec Connector works internally. For integrating a plugin, read the
[Partner Integration Guide](PARTNER_INTEGRATION.md) instead. You don't need this page for that.

---

## Hub & Micro-Gate

Node.aec keeps sign-in, activation and encryption in one **Hub** (the Connector). Each
plugin gets a local **Micro-Gate**: the `NodeAec.Licensing.Lite` NuGet package
(`Gate.Validate(slug)`). The Connector uses the same package to verify each lease before
saving it and to compute the Machine ID ([lite-adoption.md](lite-adoption.md)), so the Hub and
the plugins can never disagree about a lease. A partner plugin has no HTTP client,
no activation screens, no individual keys (`NAEC-XXXX-...`), and no encryption of its own.

```
+--------------------------------------------------------------------------+
|                              Autodesk Revit                              |
|                                                                          |
|  [ "Node.aec" Tab ]                                                      |
|                                                                          |
|  +---------------------------+      +----------------------------------+ |
|  |   Node.aec Connector      |      |  Partner Plugins (Lite package)  | |
|  |      (Central Hub)        |      |                                  | |
|  |                           |      |    public Result Execute(...)    | |
|  |  - Browser SSO (loopback) |      |    {                             | |
|  |  - Master Entitlements    |      |      var r = Gate                | |
|  |    Lease + heartbeat      |      |              .Validate(slug);    | |
|  |  - DPAPI storage          |      |      if (!r.IsLicensed)          | |
|  |  - JWKS / Ed25519         |      |          return Result.Cancelled;| |
|  |  - Tab dedup.             |      |      }                           | |
|  +---------------------------+      +----------------------------------+ |
|   |  writes                                ^  reads locally, no network  |
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
2. **No activation UI**: login, manual keys, the plugin list and Connector updates live in
   the Connector's one window, *Node.aec Connector* (opened by the only Ribbon button, **Connector**).
3. **No encryption management**: the Hub writes the lease with `CurrentUser` DPAPI, and the plugin only reads it.
4. **One sign-in**: every product on the account is synced together.
5. **Local validation with no network**, with 30 days of offline tolerance.
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
| 6 | Partner plugin runs | `Gate.Validate(slug)` (Lite) checks signature → `iss` → `scope` → `aud` → `iat` → `mid` → `exp` → `slug`. **No network.** |
| 7 | No internet | The local lease stays valid until its `exp` (30 days by default). Each online launch extends it. |

HTTP contract (endpoints, payloads, claims, error codes): [licensing-api.md](licensing-api.md).

---

## Security

- **Signature first**: the lease's Ed25519 (RFC 8032) signature is checked before any claim is trusted.
- **Compiled trust anchors**: the production public keys are compiled into
  `NodeAec.Licensing.Lite` and are the **only** keys accepted, by the Connector and by every
  plugin. No environment variable or file can replace them. The JWKS cache
  (`%APPDATA%\NodeAec\license-jwks.json`) is used to discover keys (`kid`) and signal rotation,
  but it is never trusted to verify a lease. If no usable anchor is available, the gate denies access.
- **DPAPI storage**: `entitlements.lease` and `session.json` are encrypted with
  `DataProtectionScope.CurrentUser`. A DPAPI failure never falls back to plaintext.
- **Machine binding**: the lease carries a SHA-256 of the Windows `MachineGuid`. A copied file
  fails on another computer.
- **Offline / air-gapped**: manual key entry (`NAEC-XXXX-...`). Importing `.lease` files is
  planned but not available yet.
- **Known limits** (inherent to a check that runs on the user's machine): binary patching
  of a plugin, a frozen clock while offline, an administrator cloning `MachineGuid`, and
  any process running as the same Windows user reading the decrypted lease. See
  [Partner Integration Guide §7](PARTNER_INTEGRATION.md#7-what-the-gate-protects-against-and-what-it-doesnt).
- **Diagnostics**: API errors map to stable codes. A sanitized log goes to
  `%APPDATA%\NodeAec\connector.log` (512 KB rotation, no tokens).

---

## Ribbon

The Connector owns the canonical `Node.aec` tab and its `Node.aec` panel. On startup and on
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
│   ├── Cryptography/                  # LeaseTrust — lease check and Machine ID via Lite
│   ├── Storage/                       # DPAPI persistence in %APPDATA%\NodeAec
│   ├── UI/                            # WPF windows
│   └── App.cs                         # IExternalApplication (Ribbon and dedup)
└── tests/NodeAec.Connector.Tests/     # Unit tests (net8.0, no Revit needed)
```

---

## Build matrix and packaging

Each Revit year is built against its own runtime: `net48` (2023/2024), `net8.0-windows`
(2025/2026), `net10.0-windows` (2027). One `release.ps1 -Version <x.y.z>` run builds the
three compatibility groups, each with its floor year (2023, 2025, 2027), and packs them into
one Autodesk bundle:

```
NodeAec.Connector.bundle\
  PackageContents.xml        one component per Revit year (SeriesMin = SeriesMax = R<year>)
  Contents\2023-2024\        net48 build + NodeAec.Connector.addin (relative Assembly)
  Contents\2025-2026\        net8.0-windows build
  Contents\2027\             net10.0-windows build
```

Artifacts: `release/NodeAec.Connector-<version>.bundle.zip` and
`release/NodeAec.Connector-<version>-Setup.exe` (needs [Inno Setup 6](https://jrsoftware.org/isdl.php)).

The installer copies the bundle to `%ProgramData%\Autodesk\ApplicationPlugins\`. Its year
page has one checkbox per Revit year (installed years start checked; a re-run starts from the
previous choice); it copies only the builds the checked years need and writes
`PackageContents.xml` with one component per checked year. Revit loads only the component
matching its version. Silent installs take `/RevitYears=2025,2026`. Setup also removes
installs made by the older per-group installers (`Addins\<year>\NodeAec.Connector*`), so
Revit never loads two Connectors. Uninstall removes the whole bundle.
