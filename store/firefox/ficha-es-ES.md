# Ficha de Firefox Add-ons (AMO) — Español (España)

Para el formulario «Describir complemento» de https://addons.mozilla.org/developers/. Paquete:
`C:\ID\OneDrive\Credentials\extension\sOCCredentials-extension-firefox-<versión>.zip`
(declara `data_collection_permissions: none`). La ficha en inglés, para añadir el idioma en AMO,
está en [ficha-en-US.md](ficha-en-US.md); categorías, licencia y notas para revisores son comunes y
solo están aquí.

## Nombre

```
sOC Credentials
```

## Resumen («Descripción», el corto, máx. 250 caracteres)

```
Rellena usuarios, contraseñas y códigos de segundo factor desde tu bóveda de sOC Credentials, sin servidor, con la app de tu PC.
```

## Capturas

Las mismas de Edge: `../edge/imagenes/es-ES/01…03-*.png` (1280×800).

## Descripción (la larga; admite algo de Markdown)

```
**sOC Credentials** es un gestor de contraseñas sin servidor: tu bóveda vive cifrada en tu PC (y, si quieres, en tu propio Google Drive u OneDrive). Esta extensión la conecta con Firefox.

**Qué hace**
- Al entrar en un campo de usuario o contraseña, enseña debajo las entradas que encajan con esa web y las rellena con un clic.
- Te da el código de segundo factor (TOTP) de la web, listo para copiar o rellenar.
- Ofrece guardar lo que escribes cuando entras en una web nueva.
- Generador de contraseñas en el propio botón de la barra.
- Si quieres, apaga el gestor de contraseñas de Firefox para que no te ofrezca las dos cosas a la vez.

**Cómo funciona**
La extensión no guarda nada ni habla con ningún servidor: pide las entradas a la aplicación **sOC Credentials** de tu PC por mensajería nativa. Con la bóveda cerrada solo avisa de que hay que abrirla.

**Necesita** la aplicación sOC Credentials para Windows (gratuita, código abierto): https://github.com/donki/Credentials/releases

Sin anuncios, sin telemetría, sin cuentas propias.
```

## Casillas

- **Este complemento está en fase experimental**: no.
- **Necesita pago, servicios o software de pago o hardware adicional**: no (la aplicación que
  necesita es gratuita).

## Categorías (hasta 3)

- **Privacidad y seguridad**

(Ninguna otra encaja de verdad; mejor una bien puesta que tres forzadas.)

## Correo de ayuda

*(el tuyo de soporte, el mismo que pongas en Play Console y en la Microsoft Store)*

## Página de ayuda

```
https://github.com/donki/Credentials
```

## Licencia

**MIT License** (la del repositorio).

## Política de privacidad

Marca «Este complemento tiene una política de privacidad» y pega (es la misma que
`PRIVACY.md` del repo: https://github.com/donki/Credentials/blob/master/PRIVACY.md):

```
sOC Credentials no recoge ningún dato. La extensión no guarda nada ni envía nada a internet: solo habla, dentro de tu propio ordenador, con la aplicación sOC Credentials por mensajería nativa. La bóveda se guarda cifrada en tu equipo o, si tú lo eliges, en la carpeta privada de la aplicación de tu propio Google Drive u OneDrive. No hay servidor propio, ni telemetría, ni terceros.
```

## Notas para revisores

```
This extension is the browser side of sOC Credentials, a free and open-source (MIT) password manager with no server. It does not store anything and makes no network requests: it talks only to the local desktop app through native messaging (host name "com.socratic.credentials").

To test it end to end you need the Windows app (free): https://github.com/donki/Credentials/releases — run sOCCredentials.exe (on start it registers the native messaging host for Firefox, per user, no admin rights), create a vault with any master password and add an entry for a site. Without the app, the popup shows "Hay que abrir la bóveda en sOC Credentials" (open the vault) and the extension does nothing else.

No build step and no minified or remote code: the package contents are the source (background.js, content.js, popup.html/js/css, _locales). Source repository: https://github.com/donki/Credentials (folder Extension/).

Permissions:
- nativeMessaging: talk to the local app.
- <all_urls> host permission + content script: detect login fields on any site to offer the matching entries.
- tabs / activeTab / scripting: know the current site and fill the fields when the user picks an entry.
- storage: remember the user's popup choices.
- contextMenus: "fill with sOC Credentials" on the right-click menu.
- clipboardWrite: copy usernames, passwords and TOTP codes when the user asks.
- privacy: only to turn off Firefox's own password saving if the user chooses to in the popup.
Data collection: none (data_collection_permissions: none).
```
