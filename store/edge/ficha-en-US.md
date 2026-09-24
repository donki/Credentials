# Ficha de Edge Add-ons — English (United States)

Para *Listados de la Tienda › English* de la extensión en
https://partner.microsoft.com/dashboard/microsoftedge. Paquete:
`C:\ID\OneDrive\Credentials\extension\sOCCredentials-extension-chromium-<versión>.zip`.
La pestaña de privacidad está en [privacidad.md](privacidad.md).

El **nombre** y la **descripción corta** no se escriben aquí: salen del paquete
(`Extension/common/_locales/en/messages.json`, 132 caracteres como máximo).

---

## Description (250 to 10,000 characters)

```
sOC Credentials fills in your usernames, passwords and two-factor codes on any website from your sOC Credentials vault, the free and open-source app that keeps your passwords encrypted on your PC. No server, no account and no analytics.

WHAT IT DOES
• When you enter a username or password field, it shows the matching entries for that site right below and fills them in with one click.
• Gives you the site's two-factor code (TOTP), with its countdown, ready to fill in or copy.
• When you sign in to a new site, it offers to save what you typed.
• Searches your whole vault from the toolbar button, and copies username, password or code with one click.
• Strong password generator in the same button.
• If you want, it turns off Edge's own password manager so you are not offered both at once.

HOW IT WORKS
The extension stores nothing and does not connect to the internet: it asks the sOC Credentials app on your PC for the entries through native messaging, inside your own computer. Your vault is encrypted with your master password (Argon2id + AES-256-GCM) and lives only on your PC or, if you choose, in the app's private folder of your own Google Drive or OneDrive. While the vault is closed, the extension only tells you to open it.

REQUIRES
The sOC Credentials app for Windows (free): https://github.com/donki/Credentials/releases
There is also an Android version that uses the same vault.

No ads, no telemetry and no accounts of ours. Open source under the MIT license.
```

## Search terms (up to 7, 30 characters each)

```
password manager
passwords
autofill
two-factor
TOTP
authenticator
encrypted vault
```

## Properties

| Field | Value |
|---|---|
| Category | **Productivity** |
| Website | `https://github.com/donki/Credentials` |
| Support contact | `https://github.com/donki/Credentials/issues` (or your support email) |
| Privacy policy | `https://github.com/donki/Credentials/blob/master/PRIVACY.md` |
| Mature content | No |

## Images

All in `imagenes/en-US/`, made with the real extension code and made-up data on an example site.

| Asset | Size | File |
|---|---|---|
| Store logo | 300×300 | `../microsoft/logos/icono-300x300.png` (same as the app) |
| Small promotional tile | 440×280 | `mosaico-pequeno-440x280.png` |
| Large promotional tile | 1400×560 | `mosaico-grande-1400x560.png` |
| Screenshot 1: the dropdown on a sign-in form | 1280×800 | `01-rellenar-1280x800.png` |
| Screenshot 2: the popup with entries and the TOTP code | 1280×800 | `02-popup-1280x800.png` |
| Screenshot 3: the closed-vault notice | 1280×800 | `03-boveda-cerrada-1280x800.png` |
