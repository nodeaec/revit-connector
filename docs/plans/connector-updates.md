# Plan — Connector updates and a single window

Status: approved by the maintainer on 2026-10-25. C1–C6 implemented on 2026-10-25 (Connector
side only; 224 headless tests). Still open: the platform side, then the manual checks in C5
(real endpoint, `/SILENT /WaitForRevit` with Revit open), then the 0.4.0 release. Server side: see
[`node-aec/docs/handoffs/connector-updates.md`](../../../node-aec/docs/handoffs/connector-updates.md)
(the "server contract" below is the same text).

## Decisions

- **No background update agent.** The user checks for updates from the Connector window.
  Nothing new runs at Revit startup or on a timer.
- **One Ribbon button.** The `Conector` panel keeps only **Minha Conta**. "Meus Plugins" and
  "Explorar Catálogo" move into the one window.
- **Trust:** HTTPS plus the SHA-256 the platform publishes. No Authenticode, no signed manifest.
- **Download requires sign-in.** Checking is public; downloading uses the user's session token.
- **Out of date is a clear error.** Below a minimum version the platform refuses with
  `CONNECTOR_OUTDATED`, and the window offers the update right there.
- **Badge:** the platform's existing lease responses also say what the latest and minimum
  versions are. The Connector saves that, and the window shows "Nova versão disponível"
  without the user clicking anything. No extra request: it rides on calls already made.
- **Scope:** the Connector updates itself only. Plugins (partner or not) are all products the
  same way; their updates are a later step that reuses the same endpoint per slug.

## Server contract (node-aec)

1. `GET /products/:slug/latest` — public, no auth. `404 PRODUCT_NOT_FOUND` /
   `404 NO_PUBLISHED_VERSION`. `200`:

   ```json
   {
     "slug": "nodeaec-connector",
     "version": "0.4.0",
     "releasedAt": "2026-11-02T12:00:00.000Z",
     "notes": "Texto curto da versão (pt-BR).",
     "minimumVersion": "0.3.0",
     "file": {
       "versionPublicId": "AbCdEfGhIjKlMn",
       "filePublicId": "OpQrStUvWxYz12",
       "name": "NodeAecConnector-0.4.0-Setup.exe",
       "sizeBytes": 12345678,
       "sha256": "64 lowercase hex chars"
     }
   }
   ```

   "Latest" is the highest semantic version among published versions that allow download
   and have a default file, not the newest by date. `minimumVersion` is `null` when unset.

2. Download: the existing `POST /products/:slug/versions/:versionPublicId/files/:filePublicId/download`
   with `Authorization: Bearer <user token>` → `{ "downloadUrl": "https://..." }`.

3. Minimum version: when a request's `User-Agent` is `NodeAec.Connector/<version>` and that
   version is below the configured minimum, `POST /account/entitlements/lease`,
   `POST /license/activate` and `POST /license/validate` answer
   `426 { error: true, status: 426, code: "CONNECTOR_OUTDATED", message: "<pt-BR text>",
   data: { currentVersion, minimumVersion, latestVersion } }`.
   The `message` matters: Connectors 0.3.0 and older show the server message for codes they
   do not know.

4. Badge data: those same three endpoints add to their success body
   `"connectorUpdate": { "latestVersion": "0.4.0", "minimumVersion": "0.3.0" }`
   (either may be `null`).

## Connector work

### C1. One button, one window

- `App.cs`: keep only `NodeAec_ManageConnector` (label and icon unchanged; tooltip now covers
  account, plugins, catalog and updates). Remove the stacked buttons.
- Delete `ManagePluginsCommand`, `ExploreCatalogCommand`, `RequiresLoginAvailability`,
  `PluginsWindow`, the stacked-insertion code in `RibbonDecisions` (and its tests),
  `UiTheme.PluginsIcon/CatalogIcon`, unless something else still uses them.
- `CleanRogueRibbonElements`: also remove `NodeAec_ManagePlugins` and `NodeAec_ExploreCatalog`
  (an add-in reload in the same process can leave them).
- `ConnectorWindow` becomes the only window. **`public static void ConnectorWindow.Open()` must
  keep its name, namespace and signature**: Lite's `Gate.OpenConnector()` calls it by reflection.
  Sections, top to bottom: header · Sua conta · Meus plugins (the cards from `PluginsWindow`,
  plus the catalog link) · Neste computador (status, one "Atualizar minhas licenças", key
  activation, machine ID) · Atualizações · footer.

### C2. Check for updates (section "Atualizações")

- Shows `Versão instalada: <ConnectorConfig.Version>` and one button **Verificar atualizações**.
- Click → `GET /products/nodeaec-connector/latest` off the UI thread (same pattern and
  `WindowHandlerGuard` as the other handlers, button disabled while busy). Result:
  - up to date: *"Você está na versão mais recente."*
  - newer: *"Node.aec Connector <v> disponível."* + notes + **Baixar e instalar**
  - failure/offline/404: *"Não foi possível verificar atualizações agora. Tente novamente mais tarde."*
- **Baixar e instalar**:
  - not signed in → *"Entre com sua conta para baixar a atualização."* and the sign-in button;
  - otherwise get the `downloadUrl` (Bearer), require `https://`, stream it to
    `%TEMP%\NodeAec\updates\NodeAecConnector-<version>-Setup.exe` (our own name; never the
    server's), show progress, compare size and SHA-256 (case-insensitive hex). Mismatch →
    delete the file, *"O arquivo baixado não confere. Nada foi instalado. Tente novamente."*;
  - match → start it with `/SILENT /WaitForRevit` (`UseShellExecute = true`; Windows asks for
    admin approval) and show *"Salve seu trabalho e feche o Revit. A instalação continua
    sozinha e mantém as versões do Revit que você escolheu."*
- Pure, tested pieces (no WPF/Revit types): version parse/compare (`major.minor.patch`,
  optional `v` prefix, a `-suffix` sorts below the same release, garbage → "cannot compare",
  never throws); the decision `UpToDate | Available | Required | Unknown` from current, latest,
  minimum; parsing the `latest` JSON; the hash check; the target file name.
- HTTP lives in `ConnectorApiClient` next to the other calls; JSON with `System.Text.Json`;
  errors through the existing `ParseApiErrorMessage`.

### C3. Out-of-date messages and the badge

- `ParseApiErrorMessage`: map `CONNECTOR_OUTDATED` to *"Esta versão do Node.aec Connector
  (<atual>) não é mais aceita pela plataforma. Atualize para a versão <mínima> ou mais
  recente em Atualizações."* using `data.currentVersion`/`data.minimumVersion` when present.
  Expose that the failure was `CONNECTOR_OUTDATED` (e.g. on `SyncResult`) so the window can
  run the update check and bring the Atualizações section into view.
- Badge: when a sync, activation or heartbeat response has `connectorUpdate`, save it to
  `%APPDATA%\NodeAec\update-status.json` (plain JSON, not secret, atomic write via
  `LeaseStorage.WriteAllBytesAtomic`, same base directory). On open, the window reads it and,
  if newer than the installed version, shows *"Nova versão disponível: <v>"* in the
  Atualizações section and the header; if below minimum, the outdated message. A missing or
  broken file means no badge, never an error.

### C4. Installer `/WaitForRevit` (`scripts/installer.iss`)

- Today `PrepareToInstall` refuses while `Revit.exe` runs. With `/WaitForRevit` on the command
  line it instead shows *"Feche o Revit para continuar a instalação."* (English text for the
  English language entry) and waits, checking every second, until Revit exits; then installs
  as usual. Cancel aborts with the existing "not installed" exit code. Without the switch,
  behavior is unchanged. Uninstall is unchanged.
- It must work in `/SILENT` (the message visible) and `/VERYSILENT` (no UI, just waits).
- Re-runs already keep the previous Revit years; do not change that.

### C5. Tests and acceptance

- Headless tests for every pure piece in C2/C3, the error mapping, `update-status.json`
  read/write (missing, corrupt, valid), and the removed ribbon pieces' tests deleted.
- `dotnet build NodeAec.Connector.sln -c Release` 0 errors, no new warnings;
  `dotnet test` 100% green; `ReleasePackagingTests` still pass.
- No network call or file download on the UI thread; nothing new at startup.
- Manual (maintainer, Windows): Revit 2026 — one button; the window shows account, plugins,
  update section; check against the real endpoint once it exists; install with Revit open →
  Setup waits, Revit closed → installs.

### C6. Docs (after review)

`docs/USER_MANUAL.md` (ribbon, window sections, updating, new messages, release notes),
`README.md` (ribbon), `docs/ARCHITECTURE.md`, `docs/licensing-api.md` (new endpoints and
`CONNECTOR_OUTDATED`), `AGENTS.md` stale table (update messages; the platform endpoint),
`.agents/skills/release-pack/SKILL.md` (publish the Setup.exe and its SHA-256 as a new
version of `nodeaec-connector` on the platform; set the minimum version when needed),
`docs/PARTNER_INTEGRATION.md` if it names the removed buttons.

## Release

The first Connector with this feature (0.4.0) is installed by hand. Older Connectors learn
about it from the `CONNECTOR_OUTDATED` server message once the platform sets a minimum.
