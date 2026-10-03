# User Manual — Node.aec Connector **v0.1**

> **Product:** Node.aec Connector — Governance and licensing add-in for Autodesk Revit
> **Add-in version:** 0.1.2 · **This manual's version:** 0.1 · **Date:** September 2026
> **Platform:** Windows 10/11 (64-bit) · Autodesk Revit 2023–2027 (per-year-group installer)
> **UI language:** Portuguese (Brazil)

---

## 📖 Contents

1. [What is the Node.aec Connector](#1-what-is-the-nodeaec-connector)
2. [System requirements](#2-system-requirements)
3. [Installation](#3-installation)
4. [First steps: the Node.aec tab](#4-first-steps-the-nodeaec-tab)
5. [The "Minha Conta" window](#5-the-minha-conta-window)
6. [Signing in (browser login)](#6-signing-in-browser-login)
7. [Refreshing your licenses](#7-refreshing-your-licenses)
8. [Manual key activation (NAEC keys)](#8-manual-key-activation-naec-keys)
9. [Importing a `.lease` license file (not available — see limitations)](#9-importing-a-license-lease-file)
10. [The "Meus Plugins" window](#10-the-meus-plugins-window)
11. [Browsing the catalog](#11-browsing-the-catalog)
12. [Offline mode and the 30-day grace period](#12-offline-mode-and-the-30-day-grace-period)
13. [Security and privacy](#13-security-and-privacy)
14. [Signing out and uninstalling](#14-signing-out-and-uninstalling)
15. [Troubleshooting](#15-troubleshooting)
16. [Frequently asked questions (FAQ)](#16-frequently-asked-questions-faq)
17. [Version 0.1 release notes and known limitations](#17-version-01-release-notes-and-known-limitations)
18. [Support](#18-support)

---

## 1. What Is the Node.aec Connector

The **Node.aec Connector** is Node.aec's central app inside Autodesk Revit. It concentrates, in a single place, your account **sign-in**, your license **activation**, and the **list of plugins unlocked** for your computer.

It follows the **Hub & Micro-Gate** model:

- You **sign in once** in the browser (no password typing inside Revit).
- The Connector downloads and locally saves **all of your licenses** at once.
- Node.aec and partner plugins **check the license instantly**, offline, on every command.
- You can work **up to 30 days disconnected** before needing to sync again.

### Key benefits

| Benefit | What it means for you |
|---|---|
| **Single sign-in (SSO)** | Sign in with Google/2FA in your default browser; no password is typed in Revit. |
| **Licenses in one place** | All of your products listed with visible expiration dates. |
| **Works offline** | Plugins open and validate licenses even without internet (30-day windows). |
| **Isolated (air-gapped) workstations** | Activation via `NAEC-…` key (`.lease` file import is not available yet). |
| **Tidy Ribbon** | Everything on the official **Node.aec** tab, no duplicate or ghost tabs. |

---

## 2. System Requirements

- **Operating system:** Windows 10 or Windows 11 (64-bit).
- **Autodesk Revit:** 2023 through 2027 — use the installer for your version's year group (default install at `C:\Program Files\Autodesk\Revit <year>`).
- **Internet connection:** needed **only** for the first sign-in, license refreshes, and key activations. Day-to-day plugin use needs no internet.
- **Default browser:** any browser (Chrome, Edge, Firefox…) to complete sign-in.
- **Permissions:** no administrator access is needed to use the Connector.

> 📦 The install package already includes all dependencies (including the Windows data-protection library). Nothing needs to be installed separately.

---

## 3. Installation

Installation is normally done by your company's IT/manager team, but the procedure is simple:

1. **Close Autodesk Revit** (if open).
2. Run the installer for **your Revit year group** — e.g. `NodeAec.Connector-0.1.2-R2025-2026-Setup.exe` (double-click → Next). On the **Versao do Autodesk Revit** page, pick which version to install to or keep **Todas as versoes instaladas**. The installer lists only the group years present on the computer and, if none is installed, warns and exits without changing any file. Alternative for IT teams: manually extract the `.zip` into the add-ins folder.
3. Confirm the files are in the add-ins folder:
   - Add-in folder: `C:\ProgramData\Autodesk\Revit\Addins\<year>\NodeAec.Connector\`
   - Manifest (`.addin`): `C:\ProgramData\Autodesk\Revit\Addins\<year>\NodeAec.Connector.addin`
4. **Open the installed year's Autodesk Revit.** The **Node.aec** tab appears automatically on the Ribbon.

> ✅ **Check:** when Revit opens, look for the **Node.aec** tab at the top of the Ribbon. If it does not show up, close Revit completely (including background processes) and open it again. If it persists, see [Troubleshooting](#15-troubleshooting).

> 🔄 **Updating:** run the same year's installer again (it detects the existing install) or replace the files in the add-in folder and restart Revit. Your account and licenses are preserved.
>
> 🧩 **Multiple Revit years:** each year has its own installer and its own Programs entry; installing the Connector on Revit 2026 does not affect Revit 2025 (and vice versa).

---

## 4. First Steps: The Node.aec Tab

When Revit opens, click the **Node.aec** tab. You will see the **Conector** panel with three buttons:

```
┌─────────────────────────────────────────────────────────┐
│  Node.aec                                               │
│  ┌──────────────┐  ┌──────────────┐                     │
│  │              │  │ Meus         │  ← desabilitado     │
│  │   Minha      │  │ Plugins      │    até o login      │
│  │   Conta      │  ├──────────────┤                     │
│  │              │  │ Explorar     │                     │
│  │              │  │ Catálogo     │                     │
│  └──────────────┘  └──────────────┘                     │
│         painel "Conector"                               │
└─────────────────────────────────────────────────────────┘
```

| Button | What it does | Availability |
|---|---|---|
| **Minha Conta** (large button) | Opens the account, license, and activation window. | Always available. |
| **Meus Plugins** | Opens the list of plugins linked to your account. | **Disabled until you sign in.** |
| **Explorar Catálogo** | Opens the product catalog in your browser. | Always available. |

**Recommended first-time flow:**

1. Click **Minha Conta**.
2. Click **Entrar com minha conta** and complete sign-in in the browser.
3. Back in Revit — your licenses are already unlocked.
4. Click **Meus Plugins** to see the plugins that are unlocked.

> 🧹 The Connector keeps the Ribbon tidy by itself: it removes duplicate **Node.aec** tabs and any stray tabs or buttons it finds (such as "License", "Licensing", or "Conectar Conta"). You need to do nothing for this.

---

## 5. The "Minha Conta" Window

The **Minha Conta — Node.aec** window is the heart of the Connector. It is divided into cards:

### 5.1 Header

- **"Minha Conta"** with the tagline *"Suas licenças da Node.aec em um só lugar."*
- **"Ver catálogo ↗"** link — opens the product catalog in the browser.

### 5.2 "Sua conta" Card

Two possible states:

| State | Displayed text | Buttons |
|---|---|---|
| **Disconnected** | *"Você ainda não entrou."* + *"Entre com sua conta para liberar seus plugins neste computador."* | **Entrar com minha conta** |
| **Connected** | *"Olá! Você está conectado como:"* + your **email** | **Sair da conta** |

### 5.3 "Neste computador" Card

Shows the state of your licenses saved on this machine and lets you refresh them. Possible messages:

| Message | Meaning |
|---|---|
| *"Tudo certo — suas licenças estão atualizadas até DD/MM/AAAA."* ✅ | All good; you are covered until that date. |
| *"Nenhuma licença encontrada neste computador ainda."* | You have not activated or synced anything here yet. |
| *"Suas licenças estão desatualizadas desde DD/MM/AAAA. Conecte-se à internet e clique em atualizar."* ⚠️ | The offline term has lapsed; you need to sync. |
| *"Não conseguimos ler as licenças salvas. Tente atualizar."* ⚠️ | Local file unreadable — click refresh. |

Button: **Atualizar minhas licenças** — fetches the newest licenses on your account (or renews the current ones, if you are not signed in).

### 5.4 "Tenho uma chave de ativação" Expander

Collapsed by default to keep the screen uncluttered. Click the **"Tenho uma chave de ativação"** title to open it. Inside you will find:

- **Key field** + **Ativar** button — for typing a key sent by your company (`NAEC-…` format).
- **This machine's ID (for support):** a long monospace code. **Save/copy this code when requesting support** — it uniquely identifies this computer.

### 5.5 Feedback area and footer

- Right below the cards appear the **feedback messages** (success or error) for the actions you run.
- Footer: **"Node.aec Connector 0.1"** and the **Fechar** button.

---

## 6. Signing In (Browser Login)

Sign-in uses **Browser SSO**: you never type a password inside Revit.

**Step by step:**

1. In **Minha Conta**, click **Entrar com minha conta**.
2. The *"Abrindo o navegador para você entrar com segurança…"* message appears and your **default browser** opens the Node.aec login page.
3. Sign in normally (Google account, email/password, **2FA**, etc.).
4. When done, the browser shows the **"Login Concluído com Sucesso!"** screen with the notice *"Você já pode fechar esta aba do navegador e voltar ao Revit."*
5. Back in Revit: the window shows *"Pronto! Buscando suas licenças…"* and then
   **"Tudo pronto! N plugin(s) liberado(s) neste computador."**

**What you need to know:**

- ⏱️ You have **120 seconds (2 minutes)** to complete the browser sign-in. On expiry, just click **Entrar com minha conta** again.
- The browser-to-Revit connection happens only **on your own computer** (local address `127.0.0.1`), with anti-forgery (CSRF) protection.
- After sign-in your session stays saved: you do **not** need to sign in every time you open Revit.
- If *"Algo não saiu como esperado: …"* appears, repeat sign-in; if it persists, see [Troubleshooting](#15-troubleshooting).

---

## 7. Refreshing Your Licenses

Click **Atualizar minhas licenças** when:

- A new license is released to your account;
- The message says your licenses are **out of date**;
- You want to check the latest expiration date.

**Behavior:**

- **While signed in:** the Connector re-downloads all licenses on your account → *"Licenças atualizadas com sucesso."*
- **Not signed in (active license only):** it only **renews** the local license → *"Licenças atualizadas com sucesso."*
- **No internet:** *"Sem conexão no momento: …"* or *"Não foi possível atualizar agora: …"* appears. Your licenses stay valid until the date shown on the card.

> 💡 **Automatic renewal:** every time Revit opens, the Connector renews your licenses in the background, silently and without freezing the UI. When offline that attempt fails silently — no errors getting in the way of your work.

---

## 8. Manual Key Activation (NAEC Keys)

Handy when your company provides a license key and you do not sign in with an account.

1. Open **Minha Conta**.
2. Click the **"Tenho uma chave de ativação"** expander.
3. Type the key in the indicated field. Correct format:
   `NAEC-XXXX-XXXX-XXXX-XXXX`
4. Click **Ativar**.
5. Possible messages:
   - ✅ *"Chave ativada! Seus plugins foram liberados."*
   - ⚠️ *"Digite a chave enviada para você (começa com NAEC-...)."* — the key field is empty.
   - ⚠️ Specific error message (see [Troubleshooting](#15-troubleshooting)).

> 🌐 Key activation **requires internet**, since it validates the key against the Node.aec server. For machines with no internet, talk to Node.aec support: `.lease` file import is not available in this version.

---

## 9. Importing a License (.lease) File

**This feature is unavailable in version 0.1.**

The **Minha Conta** window has no **"ou importar um arquivo de licença (.lease)"** link: the `.lease` file export/exchange format is not a stable platform contract yet. A file of unknown origin would be refused at (Ed25519) signature validation and would unlock no plugin.

**Alternatives for an isolated (air-gapped) workstation:**

1. Activate a manual `NAEC-XXXX-XXXX-XXXX-XXXX` key — activation itself does not require the lease to come over the internet, but syncing the other licenses does.
2. Sign in on an internet-connected machine to sync the licenses, then repeat the same flow on this workstation.

Signed `.lease` file import is planned for a future iteration, once the exchange format is an officially defined contract.

---

## 10. The "Meus Plugins" Window

Opened via the Ribbon's **Meus Plugins** button (after sign-in). Lists **everything your account unlocked for this computer**.

**Contents:**

- One **card per plugin**, with:
  - **Plugin name**;
  - **License status**:
    - *"Liberado até DD/MM/AAAA"* ✅ — active and valid;
    - *"Liberado — sem data para expirar"* ✅ — perpetual license;
    - *"Expirado em DD/MM/AAAA"* ⚠️ — lapsed;
    - other UPPERCASE status (e.g. `SUSPENDED`) ⚠️;
  - **"Abrir página do produto ↗"** link — opens the product site in the browser.
- **Active** plugins appear first in the list.

**Buttons and states:**

| Element | Behavior |
|---|---|
| **Entrar com minha conta** | Visible only when not signed in. Runs the same [browser login](#6-signing-in-browser-login). |
| **Atualizar lista** | Syncs again → *"Lista atualizada."* |
| Empty list | *"Nenhum plugin vinculado à sua conta ainda."* + **"Conhecer o catálogo de plugins ↗"** link. |
| Not signed in | *"Entre com sua conta para ver seus plugins aqui."* |
| Footer | "Node.aec Connector 0.1" + **Fechar** button. |

---

## 11. Browsing the Catalog

The **Explorar Catálogo** button (and the "Ver catálogo ↗" link in Minha Conta) opens the official catalog in your browser:

**https://nodeaec.com.br/products**

There you can discover available plugins, families, and templates for your account. After purchasing/activating a new product, go back to Revit and click **Atualizar minhas licenças** to unlock it.

> If the browser does not open, Revit shows: *"Node.aec Catálogo — Não foi possível abrir o navegador: …"*. Check that a default browser is set in Windows.

---

## 12. Offline Mode and the 30-Day Grace Period

The Connector is designed to **work without internet** day to day:

| Concept | Explanation |
|---|---|
| **Local license (lease)** | Your licenses stay saved and encrypted on this computer after the first sync. |
| **30-day offline grace** | The local license is valid for up to **30 days** with no server contact. Inside that window, everything works normally. |
| **Automatic renewal** | When Revit opens (or when you click *Atualizar minhas licenças*), validity is extended. |
| **Lapsed term** | *"Suas licenças estão desatualizadas desde DD/MM/AAAA…"* appears. Connect to the internet and click **Atualizar minhas licenças**. |

**Fully isolated (air-gapped) workstations:**

- Use a [manual key](#8-manual-key-activation-naec-keys) on an internet-connected machine and sync the account.
- The machine ID is fixed; the lease only works on the computer it is issued for.

---

## 13. Security and Privacy

| Aspect | How the Connector protects you |
|---|---|
| **Passwords** | Are **never** typed in Revit. Sign-in happens in your browser, with all of its security features (2FA, two-step verification). |
| **Local connection** | The browser hands sign-in back to Revit only via `127.0.0.1` (local loopback), with an anti-forgery (CSRF) token. No external server intercepts that return. |
| **Local storage** | Licenses (`entitlements.lease`) and session (`session.json`) live under `%APPDATA%\NodeAec\`, **encrypted with Windows DPAPI**, locked to your Windows user. Other users on the machine cannot read them. |
| **Machine ID** | Irreversible code (hash) derived from the Windows registry + computer name. It carries no personal data and is never sent outside a licensing context. |
| **License integrity** | Each license is issued digitally signed (Ed25519) and bound to this computer and its validity term. |
| **Communication** | Only with the official servers `https://api.nodeaec.com.br` and `https://nodeaec.com.br`. |

**Files created on your computer:**

```
%APPDATA%\NodeAec\
├── entitlements.lease   ← your licenses (encrypted)
└── session.json         ← your sign-in session (encrypted)
```

---

## 14. Signing Out and Uninstalling

### Signing out

1. **Minha Conta** → **Sair da conta** button.
2. Confirm at the prompt: *"Deseja sair da sua conta neste computador? Seus plugins ficarão bloqueados até o próximo login."*
3. Final message: *"Você saiu da conta."*

> ⚠️ On sign-out, local licenses are **removed** and Node.aec plugins stay **blocked** until you sign in again. Do this when handing the computer to someone else.

### Uninstalling

1. Close Autodesk Revit.
2. Under **Settings → Apps → Installed apps**, uninstall **Node.aec Connector - Revit <grupo>**. When more than one version has the add-in, the uninstaller asks which one to remove (or all of them); to remove it from the others, run it again. Each compatibility group (2023-2024, 2025-2026, 2027) shows as its own entry.
   Manual alternative: delete the `C:\ProgramData\Autodesk\Revit\Addins\<year>\NodeAec.Connector\` folder and the `C:\ProgramData\Autodesk\Revit\Addins\<year>\NodeAec.Connector.addin` file for each year of the group (replace `<year>` with the Revit year).
3. (Optional) Delete the `%APPDATA%\NodeAec\` folder to remove local license and session data.
4. Open Revit — the **Node.aec** tab (from the Connector) does not appear.

> ℹ️ Uninstalling the Connector does not cancel your account licenses. They remain available for reactivation on another install.

---

## 15. Troubleshooting

### Messages and how to act

| Displayed message | Likely cause | What to do |
|---|---|---|
| *"Abrindo o navegador…"* and nothing happens | Default browser unset / blocked | Set a default browser in Windows and repeat sign-in. |
| *"Falha na validação CSRF do login."* / expired login | Sign-in not completed within 120 s, or invalid return | Click **Entrar com minha conta** again and finish within 2 minutes. |
| *"Não foi possível buscar suas licenças: …"* | Server unavailable or session expired | Check the internet and sign in again. |
| *"Algo não saiu como esperado: …"* | Unexpected error in the sign-in flow | Repeat sign-in; if it persists, restart Revit. |
| *"Sem conexão no momento: …"* | No internet | Connect and click **Atualizar minhas licenças**. Your current licenses stay valid until the shown date. |
| *"Não foi possível atualizar agora: …"* | Server failure or expired session | Sign in again and refresh. |
| *"Digite a chave enviada para você (começa com NAEC-…"* | Empty key field | Type the key in `NAEC-XXXX-XXXX-XXXX-XXXX` format. |
| *"Formato de chave inválido. A chave deve seguir o formato NAEC-XXXX-XXXX-XXXX-XXXX."* | Incomplete key or typos | Copy and paste the key exactly as sent. |
| *"Chave de licença não encontrada. Verifique a digitação."* | Nonexistent key | Confirm the key with whoever sent it. |
| *"Limite de assentos simultâneos atingido para esta licença. Desative o assento em outro computador ou pelo portal web."* | All seats in use | Free a seat via the **Node.aec web portal** or on another computer. |
| *"Esta licença ou período de avaliação expirou."* | License lapsed | Renew or activate a new key. |
| *"Esta licença foi suspensa administrativamente."* | Platform suspension | Talk to your account administrator/support. |
| *"O prazo de tolerância offline (30 dias) expirou. Conecte-se à internet para sincronizar."* | 30 days without syncing | Connect to the internet and click **Atualizar minhas licenças**. |
| *"O identificador da máquina não corresponde ao registro da concessão."* | Another machine's license | Generate/activate the license **for this computer** (use the machine ID code). |
| *"Não foi possível abrir o navegador: …"* | No default browser | Set a default browser in Windows. |

### Common issues

**The Node.aec tab does not show in Revit**
1. Confirm the files are in `C:\ProgramData\Autodesk\Revit\Addins\<year>\` (for the installed year).
2. Quit Revit completely (check Task Manager) and open it again.
3. Check Revit error messages under *Exibir → Navegador de erros*.

**The "Meus Plugins" button is gray (disabled)**
→ Expected behavior: it only enables **after sign-in**. Click **Minha Conta** and sign in.

**A partner plugin shows "Node.aec — Licença Necessária"**
→ That product's license is not unlocked on this machine. Possible reasons:
- *"Nenhuma credencial do Node.aec encontrada nesta estação…"* → open **Minha Conta** and sign in (or activate a key).
- *"A concessão de licenças foi emitida para outra estação de trabalho…"* → the license belongs to another computer; activate it on this machine.
- *"O produto '…' não consta nas licenças ativas desta conta…"* → purchase/activate the product in the catalog.
- *"O limite de computadores simultâneos para '…' foi atingido."* → free a seat via the web portal.
- *"A licença ou período de teste de '…' expirou em DD/MM/AAAA."* → renew.

**Plugins blocked after "Sair da conta"**
→ Expected. Sign in again to restore licenses.

**Licenses gone after switching computers/profiles**
→ Licenses are saved **per Windows user** under `%APPDATA%\NodeAec\`. On a new machine, just [sign in again](#6-signing-in-browser-login).

---

## 16. Frequently Asked Questions (FAQ)

**Do I need to sign in every time I open Revit?**
No. After the first sign-in, the session stays saved (encrypted) on this computer.

**Do I need internet to work?**
No, as long as licenses are synced and inside the **30-day** window. Internet is only needed for sign-in, refreshes, and key activations.

**Can I use the same login on multiple computers?**
Yes, within the seat limit set for each license.

**Where do I see how long my licenses are valid?**
In **Minha Conta** → the **"Neste computador"** card (overall date) and in **Meus Plugins** (per-product date).

**I forgot my password / cannot sign in**
Authentication happens on the Node.aec page in your browser — use "Forgot password" there or talk to your account administrator.

**How do I find this machine's ID for support?**
In **Minha Conta** → the **"Tenho uma chave de ativação"** expander → the *"Identificação desta máquina (para o suporte): …"* line. Copy it and send it to support.

**Are my data sold or sent to third parties?**
No. The Connector only talks to the official Node.aec servers to validate licenses.

**Does the Connector modify my project files (.RVT)?**
No. It does not touch Revit models — only the Ribbon, licenses, and its own windows.

---

## 17. Version 0.1 Release Notes and Known Limitations

**Version 0.1 — first public release**

### What's included

- Canonical **Node.aec** tab with the **Conector** panel and automatic tab deduplication.
- **Minha Conta** window: sign-in, sign-out, license status, refresh, and manual activation.
- **Browser SSO sign-in** (local loopback, CSRF protection, 120 s window).
- **Meus Plugins** window with cards, expirations, and links per product.
- **Explorar Catálogo** button.
- **30-day** offline grace + silent renewal when Revit opens.
- Encrypted local storage (DPAPI) and machine binding.

### Known limitations in this version

- **"Meus Plugins" stays disabled before sign-in** (by design).
- **No in-Revit seat-release (deactivation) button** — to free a seat, use the **Node.aec web portal**.
- The Connector does **not install or auto-update** plugins: it unlocks the license; delivery and updates of the add-ins come from each product's own installer.
- No visual pop-up notification when the offline grace lapses — the warning appears when you open **Minha Conta**.
- The sign-in window may not bring focus back to Revit automatically; just switch windows.
- **`.lease` file import is unavailable in this version** — there is no import link in the **Minha Conta** window; the feature is planned for once the exchange format is a stable platform contract (see [section 9](#9-importing-a-license-lease-file)).
- The "N plugin(s) liberado(s)" counter refers to the last sync.

---

## 18. Support

| Channel | How to use |
|---|---|
| **Technical support** | Have at hand: the **machine ID** (Minha Conta → key expander → "Identificação desta máquina"), the version (**Node.aec Connector 0.1**), and the exact error message. |
| **Portal / catalog** | [https://nodeaec.com.br/products](https://nodeaec.com.br/products) |
| **Account area** | [https://nodeaec.com.br](https://nodeaec.com.br) |
| **Repository and issue tracker** | [github.com/nodeaec/revit-connector](https://github.com/nodeaec/revit-connector) — open an *issue* describing the step-by-step problem. |

---

*User Manual — Node.aec Connector v0.1 · Node.aec (https://nodeaec.com.br) · September 2026*
