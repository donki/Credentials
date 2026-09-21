# Ficha de Google Play — sOC Credentials

`com.socratic.credentials` · pista de pruebas cerradas (`alpha`), el día que se dé de alta.

Límites de Play: título 30 caracteres, descripción breve 80, completa 4000. El nombre en Play es
**sOC Credentials** (en el dispositivo la app se llama «Credentials»; en la Microsoft Store, «sOC
Credentials»).

---

## es-ES (idioma por defecto)

### Título

```
sOC Credentials
```

### Descripción breve

```
Contraseñas y códigos 2FA en una bóveda cifrada: en tu móvil o en tu Drive/OneDrive
```

### Descripción completa

```
sOC Credentials guarda tus contraseñas y tus códigos de segundo factor en una bóveda cifrada con una contraseña maestra que solo tú conoces. Sin servidor nuestro, sin cuenta nuestra y sin analítica: la bóveda vive en tu dispositivo o, si tú lo eliges, en la carpeta privada de la aplicación de tu propio Google Drive u OneDrive.

CÓMO SE PROTEGE
• La clave sale de tu contraseña maestra con Argon2id y todo se cifra con AES-256-GCM en tu propio dispositivo. Lo que sube a la nube ya va cifrado; nadie —nosotros tampoco— puede leerlo ni recuperar la contraseña.
• Desbloqueo con huella o cara: la clave queda en la bóveda del sistema (Keystore) y solo se libera cuando Android te verifica.
• Bloqueo automático por inactividad, vaciado del portapapeles a los segundos que elijas y pantalla protegida frente a capturas.

QUÉ GUARDA
• Sitios web y aplicaciones: usuario, contraseña (con medidor de fortaleza e historial), dirección, carpeta, etiquetas, favoritas, notas y campos extra ocultables.
• Códigos de segundo factor (TOTP/HOTP), como una app de autenticación: pega el enlace o la clave, o escanea el QR que enseña el sitio; el código se ve con su cuenta atrás y se copia con un toque.
• Notas seguras.

EN TODOS TUS DISPOSITIVOS
• Elige la misma cuenta de Google o Microsoft en el móvil y en el PC (también hay versión para Windows) y usa la misma contraseña maestra: los cambios se mezclan entrada a entrada, gana la más reciente.
• Autocompletar de Android: en las apps y en el navegador, sOC Credentials te propone las credenciales que casan con el sitio o con la app.

TRAE LO QUE YA TIENES
• Importa el CSV de Chrome, Edge, Brave, Firefox o Safari, Bitwarden, KeePass y KeePassXC; el JSON de Aegis o 2FAS; y las cuentas de Google Authenticator escaneando su QR de exportación.
• Exporta la bóveda cifrada cuando quieras.

Generador de contraseñas con longitud y juegos de caracteres a tu gusto. Software libre bajo licencia MIT. En castellano y en inglés, con modo claro y oscuro.
```

---

## en-US

### Título

```
sOC Credentials
```

### Descripción breve

```
Passwords and 2FA codes in an encrypted vault: on your phone or your Drive/OneDrive
```

### Descripción completa

```
sOC Credentials keeps your passwords and two-factor codes in a vault encrypted with a master password only you know. No server of ours, no account of ours, no analytics: the vault lives on your device or, if you choose so, in the private app folder of your own Google Drive or OneDrive.

HOW IT IS PROTECTED
• The key is derived from your master password with Argon2id and everything is encrypted with AES-256-GCM on your own device. What goes to the cloud is already encrypted; nobody — not even us — can read it or recover the password.
• Unlock with fingerprint or face: the key stays in the system vault (Keystore) and is only released once Android verifies you.
• Automatic lock after inactivity, clipboard cleared after the seconds you choose, and a screen protected against screenshots.

WHAT IT STORES
• Websites and apps: user name, password (with strength meter and history), address, folder, tags, favourites, notes and extra fields that can be hidden.
• Two-factor codes (TOTP/HOTP), like an authenticator app: paste the link or the key, or scan the QR the site shows; the code is displayed with its countdown and copied with one tap.
• Secure notes.

ON ALL YOUR DEVICES
• Pick the same Google or Microsoft account on your phone and on your PC (there is a Windows version too) and use the same master password: changes are merged entry by entry, newest wins.
• Android autofill: in apps and in the browser, sOC Credentials offers the credentials that match the site or the app.

BRING WHAT YOU ALREADY HAVE
• Import the CSV from Chrome, Edge, Brave, Firefox or Safari, Bitwarden, KeePass and KeePassXC; the JSON from Aegis or 2FAS; and your Google Authenticator accounts by scanning its export QR.
• Export the encrypted vault whenever you want.

Password generator with the length and character sets you like. Free software under the MIT license. In English and Spanish, with light and dark mode.
```

---

## Imágenes

| Qué | Fichero | Estado |
|---|---|---|
| Icono 512×512 | `icon_512.png` | Hecho (el mismo dibujo que `appicon.svg`). |
| Gráfico destacado 1024×500 | `feature_graphic.png` | Hecho. |
| Capturas de teléfono | `capturas/es-ES/` | Hechas en Android (emulador Pixel 6, 1080×2400, tema oscuro) con datos inventados (gancho de Debug `--es master … --ez demo true`): bóveda, detalle con TOTP, ajustes y desbloqueo. |

## Lo que Play pide aparte de esto

- **Seguridad de los datos**: no se recogen datos; el usuario puede elegir guardar su bóveda cifrada
  en su propia cuenta de Google Drive u OneDrive (acceso solo a la carpeta de la aplicación).
- **Permisos**: cámara (leer QR), internet (solo con la bóveda en la nube), biometría.
- **Servicio de autocompletar**: declarar que la app es un gestor de contraseñas.
- **Política de privacidad**: la página del catálogo con el párrafo específico: «sOC Credentials no
  recoge ningún dato. La bóveda se guarda cifrada en el dispositivo o, si el usuario lo elige, en la
  carpeta privada de la aplicación de su propio Google Drive u OneDrive; la aplicación no tiene
  servidor propio ni envía nada a terceros.»
- Antes de nada, **crear la aplicación en Play Console** (no se puede por API) con el paquete
  `com.socratic.credentials`, clave de subida propia o la compartida (ver «Dos keystores»).
