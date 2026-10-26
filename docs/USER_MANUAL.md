# User Manual — Node.aec Connector

> **Product:** Node.aec Connector — Governance and licensing add-in for Autodesk Revit
> **Applies to:** Connector 0.4.0 (the version is shown in the footer of the Connector window; 0.3.0 has three Ribbon buttons and no Atualizações section) · **Updated:** October 2026
> **Platform:** Windows 10/11 (64-bit) · Autodesk Revit 2023–2027 (one installer for every version)
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
10. [The "Meus plugins" section](#10-the-meus-plugins-section)
11. [Updating the Connector](#11-updating-the-connector)
12. [Browsing the catalog](#12-browsing-the-catalog)
13. [Offline mode and the 30-day grace period](#13-offline-mode-and-the-30-day-grace-period)
14. [Security and privacy](#14-security-and-privacy)
15. [Signing out and uninstalling](#15-signing-out-and-uninstalling)
16. [Troubleshooting](#16-troubleshooting)
17. [Frequently asked questions (FAQ)](#17-frequently-asked-questions-faq)
18. [Release notes and known limitations](#18-release-notes-and-known-limitations)
19. [Support](#19-support)

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
- **Autodesk Revit:** 2023 through 2027. One installer covers every version.
- **Internet connection:** needed **only** for the first sign-in, license refreshes, and key activations. Day-to-day plugin use needs no internet.
- **Default browser:** any browser (Chrome, Edge, Firefox…) to complete sign-in.
- **Permissions:** installing needs administrator rights (the installer writes to `C:\ProgramData`); using the Connector does not.

> 📦 The install package already includes all dependencies (including the Windows data-protection library). Nothing needs to be installed separately.

---

## 3. Installation

Installation is normally done by your company's IT/manager team, but the procedure is simple:

1. **Close Autodesk Revit** (if open). The installer refuses to continue while Revit is running, because it has to replace files Revit keeps in use.
2. Run `NodeAec.Connector-0.4.0-Setup.exe` (double-click → Next).
3. On the **Versoes do Autodesk Revit** page, check the Revit versions that should load the Connector. Versions installed on the computer are already checked and marked *instalado*. You can also check a version you haven't installed yet. At least one version must be checked.
4. Finish the wizard. The Connector is installed in `C:\ProgramData\Autodesk\ApplicationPlugins\NodeAec.Connector.bundle\`.
5. **Open Autodesk Revit.** The **Node.aec** tab appears automatically on the Ribbon.

Alternative for IT teams: extract `NodeAec.Connector-<version>.bundle.zip` into `C:\ProgramData\Autodesk\ApplicationPlugins\` (it enables every version), or run the installer silently: `NodeAec.Connector-0.4.0-Setup.exe /VERYSILENT /RevitYears=2025,2026`.

> ✅ **Check:** when Revit opens, look for the **Node.aec** tab at the top of the Ribbon. If it does not show up, close Revit completely (including background processes) and open it again. If it persists, see [Troubleshooting](#16-troubleshooting).

> 🔄 **Updating:** use **Verificar atualizações** in the Connector window (see [Updating the Connector](#11-updating-the-connector)), or close Revit and run the new installer. Either way it keeps the versions you chose last time, and your account and licenses are preserved.
>
> 🧩 **Adding or removing a Revit version:** run the installer again and change the checkboxes.
>
> ♻️ **Upgrading from 0.2.0 or older:** those versions had one installer per Revit group. The new installer removes them automatically, so you end up with a single **Node.aec Connector** entry in Windows Settings → Apps.

---

## 4. First Steps: The Node.aec Tab

When Revit opens, click the **Node.aec** tab. You will see the **Conector** panel with one button:

```
┌──────────────────────────────┐
│  Node.aec                    │
│  ┌──────────────┐            │
│  │              │            │
│  │   Minha      │            │
│  │   Conta      │            │
│  │              │            │
│  └──────────────┘            │
│      painel "Conector"       │
└──────────────────────────────┘
```

**Minha Conta** opens the Connector window, where everything happens: your account, your
plugins, the licenses on this computer, key activation, the catalog, and updates. It is
always available.

**Recommended first-time flow:**

1. Click **Minha Conta**.
2. Click **Entrar com minha conta** and complete sign-in in the browser.
3. Back in Revit — your licenses are already unlocked, and the **Meus plugins** section lists them.

> 🧹 The Connector keeps the Ribbon tidy by itself: it removes duplicate **Node.aec** tabs and any stray tabs or buttons it finds (such as "License", "Licensing", "Conectar Conta", or the "Meus Plugins" and "Explorar Catálogo" buttons of older versions). You need to do nothing for this.

---

## 5. The "Minha Conta" Window

The **Minha Conta — Node.aec** window is the only Connector window. From top to bottom:
header, **Sua conta**, **Meus plugins**, **Neste computador** (with the key expander),
**Atualizações**, and the footer.

### 5.1 Header

- **"Minha Conta"** with the tagline *"Suas licenças da Node.aec em um só lugar."*
- **"Ver catálogo ↗"** link — opens the product catalog in the browser.
- An update notice, when the Connector knows of one: *"Nova versão disponível: X.Y.Z"* or
  *"Atualização obrigatória"*. See [Updating the Connector](#11-updating-the-connector).

### 5.2 "Sua conta" Card

Two possible states:

| State | Displayed text | Buttons |
|---|---|---|
| **Disconnected** | *"Você ainda não entrou."* + *"Entre com sua conta para liberar seus plugins neste computador."* | **Entrar com minha conta** |
| **Connected** | *"Olá! Você está conectado como:"* + your **email** | **Sair da conta** |

### 5.3 "Meus plugins" Card

Lists the plugins your account unlocked. Details in [section 10](#10-the-meus-plugins-section).

### 5.4 "Neste computador" Card

Shows the state of your licenses saved on this machine and lets you refresh them. Possible messages:

| Message | Meaning |
|---|---|
| *"Tudo certo — suas licenças estão atualizadas até DD/MM/AAAA."* ✅ | All good; you are covered until that date. |
| *"Nenhuma licença encontrada neste computador ainda."* | You have not activated or synced anything here yet. |
| *"Suas licenças estão desatualizadas desde DD/MM/AAAA. Conecte-se à internet e clique em atualizar."* ⚠️ | The offline term has lapsed; you need to sync. |
| *"Não conseguimos ler as licenças salvas. Tente atualizar."* ⚠️ | Local file unreadable — click refresh. |

Button: **Atualizar minhas licenças** — fetches the newest licenses on your account (or renews the current ones, if you are not signed in).

### 5.5 "Tenho uma chave de ativação" Expander

Collapsed by default to keep the screen uncluttered. Click the **"Tenho uma chave de ativação"** title to open it. Inside you will find:

- **Key field** + **Ativar** button — for typing a key sent by your company (`NAEC-…` format).
- **This machine's ID (for support):** a long monospace code. **Save/copy this code when requesting support** — it uniquely identifies this computer.

### 5.6 "Atualizações" Card

Shows *"Versão instalada: X.Y.Z"* and the **Verificar atualizações** button. Details in
[section 11](#11-updating-the-connector).

### 5.7 Feedback area and footer

- Right below the cards appear the **feedback messages** (success or error) for the actions you run.
- Footer: **"Node.aec Connector <version>"** (for example *0.4.0*) and the **Fechar** button.

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
- If *"Ocorreu um erro inesperado (…). Tente novamente; se persistir, reinicie o Revit."* appears, repeat sign-in; if it persists, see [Troubleshooting](#16-troubleshooting).

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
- **Licenses saved, verification keys not refreshed:** after a sync the window may show *"Suas licenças foram atualizadas, mas a renovação das chaves de verificação falhou agora; as chaves já salvas continuam valendo."* Nothing to do; the next refresh retries. If it says *"Suas licenças foram salvas, mas as chaves de verificação não puderam ser obtidas — os plugins podem continuar bloqueados até a próxima sincronização bem-sucedida."*, connect to the internet and click **Atualizar minhas licenças** again.

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
   - ✅ *"Chave ativada! N plugin(s) liberado(s) neste computador."* — the key is active and your licenses were downloaded.
   - ✅ *"Chave ativada! Entre com sua conta para trazer suas licenças para este computador."* — the key is active; sign in to download the licenses.
   - ⚠️ *"Chave ativada, mas não foi possível atualizar as licenças agora: …"* — click **Atualizar minhas licenças** when you are back online.
   - ⚠️ *"Digite a chave enviada para você (começa com NAEC-...)."* — the key field is empty.
   - ⚠️ Specific error message (see [Troubleshooting](#16-troubleshooting)).

> 🌐 Key activation **requires internet**, since it validates the key against the Node.aec server. For machines with no internet, talk to Node.aec support: `.lease` file import is not available in this version.

---

## 9. Importing a License (.lease) File

**This feature is not available yet.**

The **Minha Conta** window has no **"ou importar um arquivo de licença (.lease)"** link: the `.lease` file export/exchange format is not a stable platform contract yet. A file of unknown origin would be refused at (Ed25519) signature validation and would unlock no plugin.

**Alternatives for an isolated (air-gapped) workstation:**

1. Activate a manual `NAEC-XXXX-XXXX-XXXX-XXXX` key — activation itself does not require the lease to come over the internet, but syncing the other licenses does.
2. Sign in on an internet-connected machine to sync the licenses, then repeat the same flow on this workstation.

Signed `.lease` file import is planned for a future iteration, once the exchange format is an officially defined contract.

---

## 10. The "Meus plugins" Section

The **Meus plugins** card of the Connector window lists **everything your account unlocked for this computer**.

**Contents:**

- One **card per plugin**, with:
  - **Plugin name**;
  - **License status**:
    - *"Liberado até DD/MM/AAAA"* ✅ — active and valid;
    - *"Liberado — sem data para expirar"* ✅ — perpetual license;
    - *"Expirado em DD/MM/AAAA"* ⚠️ — lapsed;
    - other UPPERCASE status (e.g. `SUSPENDED`) ⚠️;
  - **"Abrir página do produto ↗"** link — opens the product site in the browser.
- **Active** plugins appear first in the list, followed by the **"Explorar catálogo completo no marketplace ↗"** link.

**Other states:**

| State | What the card shows |
|---|---|
| Not signed in | *"Entre com sua conta para ver seus plugins aqui."* Sign in in the **Sua conta** card. |
| Signed in, no plugins | *"Nenhum plugin vinculado à sua conta ainda."* + **"Conhecer o catálogo de plugins ↗"** link. |

The list follows the last sync: click **Atualizar minhas licenças** to refresh it.

---

## 11. Updating the Connector

The Connector checks for a new version only when you ask; nothing runs in the background
for this. You need internet to check, and you need to be signed in to download.

1. Open **Minha Conta** and scroll to **Atualizações**.
2. Click **Verificar atualizações**. Possible answers:
   - *"Você está na versão mais recente."* — nothing to do.
   - *"Node.aec Connector X.Y.Z disponível."* with the version notes and a **Baixar e instalar** button.
   - *"Esta versão do Node.aec Connector (…) não é mais aceita pela plataforma. Atualize para a versão … ou mais recente."* — the update is required; see below.
   - *"Não foi possível verificar atualizações agora. Tente novamente mais tarde."* — no internet or the platform did not answer.
3. Click **Baixar e instalar**. The Connector downloads the installer (*"Baixando atualização... N%"*) and
   checks that the file is exactly the one Node.aec published (*"Verificando integridade do arquivo..."*).
4. Windows asks for administrator approval for the installer.
5. *"Salve seu trabalho e feche o Revit. A instalação continua sozinha e mantém as versões do Revit que você escolheu."*
   The installer waits with the message *"Feche o Revit para continuar a instalacao."*; as soon as you close every
   Revit window, it installs. Open Revit again to use the new version.

**Messages while downloading:**

| Message | What to do |
|---|---|
| *"Entre com sua conta para baixar a atualização."* | Sign in in the **Sua conta** card, then click **Baixar e instalar** again. |
| *"Não foi possível obter o link de download. Tente novamente mais tarde."* | Try again later; if it persists, download the installer from the [product page](https://nodeaec.com.br/products/nodeaec-connector). |
| *"Não foi possível concluir o download. Verifique sua conexão e tente novamente."* | Check the internet and try again. |
| *"O arquivo baixado não confere. Nada foi instalado. Tente novamente."* | The file did not match what Node.aec published, so it was deleted. Try again; if it persists, contact support. |
| *"A instalação foi cancelada. Clique em Baixar e instalar para tentar de novo."* | You declined the Windows administrator prompt. |
| Clicking **Cancel** in the waiting installer | The installer stops without changing anything. Run the update again later. |

**Update notice.** When the Connector renews your licenses (at Revit startup, every 6 hours,
or when you click **Atualizar minhas licenças**), the platform also tells it the newest
version. The window then shows *"Nova versão disponível: X.Y.Z"* in the header and in
**Atualizações**, without you clicking anything.

**Required updates.** Node.aec may stop accepting very old Connectors. Then signing in,
refreshing licenses or activating a key fails with *"Esta versão do Node.aec Connector (…)
não é mais aceita pela plataforma. Atualize para a versão … ou mais recente em
Atualizações."*, the header shows *"Atualização obrigatória"*, and the window checks for
the update right away. Your plugins keep working offline until the local license expires
(see [offline mode](#13-offline-mode-and-the-30-day-grace-period)), so update soon.

> ℹ️ Connector 0.3.0 and older have no **Atualizações** section: they show the platform's
> message, *"Esta versão do Node.aec Connector não é mais aceita. Baixe a versão mais recente
> em nodeaec.com.br/products/nodeaec-connector."*, and you install the new version from the
> [product page](https://nodeaec.com.br/products/nodeaec-connector).

---

## 12. Browsing the Catalog

The **"Ver catálogo ↗"** link in the window header (and the catalog links in **Meus plugins**) opens the official catalog in your browser:

**https://nodeaec.com.br/products**

There you can discover available plugins, families, and templates for your account. After purchasing/activating a new product, go back to Revit and click **Atualizar minhas licenças** to unlock it.

> If the browser does not open, the window shows *"Não foi possível abrir o link (…)."*. Check that a default browser is set in Windows.

---

## 13. Offline Mode and the 30-Day Grace Period

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

## 14. Security and Privacy

| Aspect | How the Connector protects you |
|---|---|
| **Passwords** | Are **never** typed in Revit. Sign-in happens in your browser, with all of its security features (2FA, two-step verification). |
| **Local connection** | The browser hands sign-in back to Revit only via `127.0.0.1` (local loopback), with an anti-forgery (CSRF) token. No external server intercepts that return. |
| **Local storage** | Licenses (`entitlements.lease`) and session (`session.json`) live under `%APPDATA%\NodeAec\`, **encrypted with Windows DPAPI**, locked to your Windows user. Other users on the machine cannot read them. |
| **Machine ID** | Irreversible code: a SHA-256 hash of the Windows `MachineGuid` (a random identifier Windows creates at install). It carries no personal data and is never sent outside a licensing context. |
| **License integrity** | Each license is issued digitally signed (Ed25519) and bound to this computer and its validity term. |
| **Communication** | Only with the official servers `https://api.nodeaec.com.br` and `https://nodeaec.com.br`, plus the HTTPS download link the platform gives for an update. |
| **Updates** | Downloaded only when you click **Baixar e instalar**, only over HTTPS, and run only if the file's size and SHA-256 match what Node.aec published. Installers wait in `%TEMP%\NodeAec\updates\`. |

**Files created on your computer:**

```
%APPDATA%\NodeAec\
├── entitlements.lease   ← your licenses (encrypted)
├── session.json         ← your sign-in session (encrypted)
└── update-status.json   ← newest and minimum Connector versions (not secret, not encrypted)
```

---

## 15. Signing Out and Uninstalling

### Signing out

1. **Minha Conta** → **Sair da conta** button.
2. Confirm at the prompt: *"Deseja sair da sua conta neste computador? Seus plugins ficarão bloqueados até o próximo login."*
3. Final message: *"Você saiu da conta."*

> ⚠️ On sign-out, local licenses are **removed** and Node.aec plugins stay **blocked** until you sign in again. Do this when handing the computer to someone else.

### Uninstalling

1. Close Autodesk Revit.
2. Under **Settings → Apps → Installed apps**, uninstall **Node.aec Connector**. This removes it from every Revit version. To remove it from only some versions, run the installer again and uncheck them instead.
   Manual alternative: delete the `C:\ProgramData\Autodesk\ApplicationPlugins\NodeAec.Connector.bundle\` folder.
3. (Optional) Delete the `%APPDATA%\NodeAec\` folder to remove local license and session data.
4. Open Revit — the **Node.aec** tab (from the Connector) does not appear.

> ℹ️ Uninstalling the Connector does not cancel your account licenses. They remain available for reactivation on another install.

---

## 16. Troubleshooting

### Messages and how to act

| Displayed message | Likely cause | What to do |
|---|---|---|
| *"Abrindo o navegador…"* and nothing happens | Default browser unset / blocked | Set a default browser in Windows and repeat sign-in. |
| *"Tempo esgotado aguardando a confirmação do login no navegador…"* | Sign-in not completed within 120 s | Click **Entrar com minha conta** again and finish within 2 minutes. |
| The browser tab shows *"Falha na autenticação: Estado inválido ou token ausente."* | The sign-in return was invalid (an old or copied link) | Close the tab and click **Entrar com minha conta** again. |
| *"Não foi possível abrir o navegador padrão: …"* | No default browser | Set a default browser in Windows and repeat sign-in. |
| *"Não foi possível buscar suas licenças: …"* | Server unavailable or session expired | Check the internet and sign in again. |
| *"Ocorreu um erro inesperado (…). Tente novamente; se persistir, reinicie o Revit."* | Unexpected error in the sign-in or refresh flow | Repeat the action; if it persists, restart Revit. |
| *"Não foi possível abrir a porta local de login…"* | Another sign-in window holds the local port, or local network access is restricted | Close other sign-in windows and try again; on a managed computer, ask IT to allow local (`127.0.0.1`) connections. |
| *"Sua sessão expirou. Entre com sua conta novamente."* | The sign-in session ended | Click **Entrar com minha conta**. |
| *"Muitas tentativas em pouco tempo. Aguarde alguns minutos e tente novamente."* | Too many requests in a short time | Wait a few minutes. |
| *"Sem conexão no momento: …"* | No internet | Connect and click **Atualizar minhas licenças**. Your current licenses stay valid until the shown date. |
| *"Não foi possível atualizar agora: …"* | Server failure or expired session | Sign in again and refresh. |
| *"Digite a chave enviada para você (começa com NAEC-…"* | Empty key field | Type the key in `NAEC-XXXX-XXXX-XXXX-XXXX` format. |
| *"Formato de chave inválido. A chave deve seguir o formato NAEC-XXXX-XXXX-XXXX-XXXX."* | Incomplete key or typos | Copy and paste the key exactly as sent. |
| *"Chave de licença não encontrada. Verifique a digitação."* | Nonexistent key | Confirm the key with whoever sent it. |
| *"Limite de assentos simultâneos atingido para esta licença. Desative o assento em outro computador ou pelo portal web."* | All seats in use | Free a seat via the **Node.aec web portal** or on another computer. |
| *"Esta licença ou período de avaliação expirou."* | License lapsed | Renew or activate a new key. |
| *"Esta licença foi suspensa administrativamente. Fale com o suporte do Node.aec."* | Platform suspension | Talk to your account administrator/support. |
| *"Esta licença foi cancelada ou reembolsada. Libere outra chave."* | License revoked or refunded | Ask for a new key. |
| *"O período de avaliação já foi usado neste computador. Contrate uma assinatura comercial."* | The trial was already used here | Buy a commercial license. |
| *"Este computador ainda não está registrado nesta licença. Ative a chave primeiro."* | The key was never activated on this computer | Activate the key in **Tenho uma chave de ativação**. |
| *"O prazo de tolerância offline expirou. Conecte-se à internet para sincronizar."* | 30 days without syncing | Connect to the internet and click **Atualizar minhas licenças**. |
| *"A licença local pertence a outro computador. Entre com sua conta para ativar este equipamento."* | The local license was issued for another machine | Sign in (or activate the key) on this computer. |
| *"Não foi possível abrir o link (…)."* | No default browser | Set a default browser in Windows. |
| *"Esta versão do Node.aec Connector (…) não é mais aceita pela plataforma…"* | The Connector is older than the platform accepts | Update in **Atualizações** ([section 11](#11-updating-the-connector)). |

### Common issues

**The Node.aec tab does not show in Revit**
1. Confirm the folder `C:\ProgramData\Autodesk\ApplicationPlugins\NodeAec.Connector.bundle\` exists, and that its `PackageContents.xml` lists your Revit version (`SeriesMin="R2026"` for Revit 2026). If not, run the installer again and check that version.
2. Quit Revit completely (check Task Manager) and open it again.
3. Check Revit error messages under *Exibir → Navegador de erros*.

**Only one Node.aec button, where are "Meus Plugins" and "Explorar Catálogo"?**
→ Since 0.4.0 they are sections of the **Minha Conta** window.

**A partner plugin says "Licença necessária"**
→ That product's license is not unlocked on this machine. The dialog shows exactly one reason, in Portuguese or in English (it follows Revit's language). The most common ones:
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

## 17. Frequently Asked Questions (FAQ)

**Do I need to sign in every time I open Revit?**
No. After the first sign-in, the session stays saved (encrypted) on this computer.

**Do I need internet to work?**
No, as long as licenses are synced and inside the **30-day** window. Internet is only needed for sign-in, refreshes, and key activations.

**Can I use the same login on multiple computers?**
Yes, within the seat limit set for each license.

**Where do I see how long my licenses are valid?**
In **Minha Conta** → the **"Neste computador"** card (overall date) and the **"Meus plugins"** card (per-product date).

**I forgot my password / cannot sign in**
Authentication happens on the Node.aec page in your browser — use "Forgot password" there or talk to your account administrator.

**How do I find this machine's ID for support?**
In **Minha Conta** → the **"Tenho uma chave de ativação"** expander → the *"Identificação desta máquina (para o suporte): …"* line. Copy it and send it to support.

**Are my data sold or sent to third parties?**
No. The Connector only talks to the official Node.aec servers to validate licenses.

**Does the Connector update itself?**
Only when you click **Verificar atualizações** and then **Baixar e instalar**. It never downloads or installs anything on its own.

**Does the Connector modify my project files (.RVT)?**
No. It does not touch Revit models — only the Ribbon, licenses, and its own windows.

---

## 18. Release Notes and Known Limitations

### 0.4.0 (October 2026)

- **One Ribbon button.** **Minha Conta** opens the only Connector window, which now also holds the plugin list (**Meus plugins**) and the catalog links. The **Meus Plugins** and **Explorar Catálogo** buttons are gone.
- **Atualizações** section: **Verificar atualizações** checks for a new Connector; **Baixar e instalar** downloads it, verifies it, and installs it when you close Revit.
- Update notice in the window when a newer version exists, and a clear message when the platform requires an update.
- **Meus plugins** no longer lists the Connector itself (downloading it from the platform adds it to your account as a free product).

### 0.3.0 (October 2026)

- **One installer for every Revit version (2023–2027)**, with a checkbox per version. Run it again to add or remove versions. It replaces the per-version installers of 0.2.0 and removes them automatically.
- The Connector now installs as an Autodesk bundle in `C:\ProgramData\Autodesk\ApplicationPlugins\NodeAec.Connector.bundle\`.
- License checks use the same verification package (`NodeAec.Licensing.Lite`) as partner plugins, so a license the Connector accepts is one every plugin accepts.

### 0.2.0 (October 2026)

- One installer per group of Revit versions (2023–2024, 2025–2026, 2027).
- Licenses are verified with digital signatures before they are saved.
- `.lease` file import removed; licenses are refreshed automatically after a key activation.

### 0.1 (September 2026) — first public release

- Canonical **Node.aec** tab with the **Conector** panel and automatic tab deduplication.
- **Minha Conta** window: sign-in, sign-out, license status, refresh, and manual activation.
- **Browser SSO sign-in** (local loopback, CSRF protection, 120 s window).
- **Meus Plugins** window with cards, expirations, and links per product.
- **Explorar Catálogo** button.
- **30-day** offline grace + silent renewal when Revit opens.
- Encrypted local storage (DPAPI) and machine binding.

### Known limitations

- Downloading an update requires signing in; checking does not.
- **No in-Revit seat-release (deactivation) button** — to free a seat, use the **Node.aec web portal**.
- The Connector updates **only itself**, and only when you ask. It does not install or update plugins: delivery and updates of the add-ins come from each product's own installer.
- No visual pop-up notification when the offline grace lapses — the warning appears when you open **Minha Conta**.
- The sign-in window may not bring focus back to Revit automatically; just switch windows.
- **`.lease` file import is unavailable in this version** — there is no import link in the **Minha Conta** window; the feature is planned for once the exchange format is a stable platform contract (see [section 9](#9-importing-a-license-lease-file)).
- The "N plugin(s) liberado(s)" counter refers to the last sync.

---

## 19. Support

| Channel | How to use |
|---|---|
| **Technical support** | Have at hand: the **machine ID** (Minha Conta → key expander → "Identificação desta máquina"), the version (shown in the window footer and in **Atualizações**, e.g. **Node.aec Connector 0.4.0**), and the exact error message. |
| **Portal / catalog** | [https://nodeaec.com.br/products](https://nodeaec.com.br/products) |
| **Account area** | [https://nodeaec.com.br](https://nodeaec.com.br) |
| **Repository and issue tracker** | [github.com/nodeaec/revit-connector](https://github.com/nodeaec/revit-connector) — open an *issue* describing the step-by-step problem. |

---

*User Manual — Node.aec Connector · Node.aec (https://nodeaec.com.br) · October 2026*
