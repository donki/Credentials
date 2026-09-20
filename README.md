# sOC Credentials

Gestor de credenciales y de códigos de segundo factor (TOTP) para **Android y Windows**, en .NET
MAUI (el mismo proyecto para los dos). Todo va en una **bóveda cifrada con tu contraseña maestra**
que vive en tu aparato o, si lo eliges, en la carpeta privada de la aplicación de **tu propio Google
Drive u OneDrive**. Sin servidor nuestro, sin cuenta nuestra, sin analítica. Cumple la Constitución
de Proyectos de Software de sOCratic.

## Dónde conseguirla

- **Google Play:** https://play.google.com/store/apps/details?id=com.socratic.credentials (cuando
  esté publicada).
- **Microsoft Store:** «sOC Credentials» (en cuanto Partner Center dé el enlace).
- **Releases de GitHub** (APK / EXE / MSIX de cada versión): https://github.com/donki/Credentials/releases

## Qué hace

- **Bóveda cifrada**: clave derivada de la contraseña maestra con **Argon2id** (3 pasadas, 64 MB)
  y cifrado **AES-256-GCM**; la cabecera (parámetros de derivación) va autenticada. Formato de
  fichero de texto (`soccred1` + cabecera JSON + base64), `Services/VaultCrypto.cs`.
- **Dónde vive**: solo en el aparato, o en **Google Drive** (`appDataFolder`, ámbito
  `drive.appdata`) o **OneDrive** (`special/approot`, ámbito `Files.ReadWrite.AppFolder`). La copia
  de trabajo es siempre el fichero local; la nube se baja al abrir y se sube tras cada cambio, y se
  **mezcla por entrada** (gana la más nueva; las bajas se propagan como borrado lógico 90 días).
- **Desbloqueo** con contraseña maestra o con **Windows Hello / huella**: la clave derivada queda
  en la bóveda del sistema (`SecureStorage`: DPAPI en Windows, Keystore en Android) y solo se lee
  tras la verificación del sistema. Bloqueo por inactividad y vaciado del portapapeles.
- **Entradas**: sitio web, aplicación, código de segundo factor y nota segura; usuario, contraseña
  (con fortaleza e historial), URL, carpeta, etiquetas, favorita, notas y campos extra ocultables.
- **TOTP/HOTP** (RFC 6238/4226, SHA-1/256/512, 6–10 dígitos, periodo): pegando el `otpauth://`,
  la clave o **escaneando el QR** con la cámara (ZXing). Código vivo con cuenta atrás y copiar.
- **Generador de contraseñas** con longitud y juegos de caracteres, aleatoriedad criptográfica.
- **Importar** de los navegadores y otros gestores (`Services/Importers.cs`): CSV de **Chrome,
  Edge, Brave, Opera, Vivaldi** («Exportar contraseñas»), **Firefox**, **Safari**, **Bitwarden**
  (CSV y JSON), **KeePass 2** y **KeePassXC**; JSON de **Aegis** y **2FAS**; el QR de exportación de
  **Google Authenticator** (`otpauth-migration://`, protobuf decodificado a mano); y el JSON en
  claro de la propia aplicación. Las repetidas se saltan.
- **Exportar** la bóveda cifrada (`.soccred`) o en JSON en claro (con aviso).
- **Autocompletar de Android** (`Platforms/Android/AutofillService.cs`): en apps y navegadores
  ofrece las entradas que casan con el dominio o con el paquete; con la bóveda bloqueada, la
  sugerencia abre la puerta de desbloqueo (biometría o contraseña) y luego rellena. Se activa en
  Ajustes de Android › Servicio de autocompletar.
- **Autocompletar de Android: guardar**: al enviar un formulario con usuario y contraseña, Android
  pregunta «¿Guardar la contraseña en Credentials?»; entrada nueva o cambio de contraseña (la
  anterior al historial), con desbloqueo previo si hace falta.
- **Extensiones de navegador (Windows)** para **Edge, Chrome y Firefox** (`Extension/`): el icono
  de la barra enseña las entradas del sitio con **rellenar**, copiar usuario/contraseña y el
  **código TOTP** en vivo, busca en toda la bóveda, genera contraseñas y, al enviar un formulario
  nuevo, **ofrece guardarlo** (o actualizar la contraseña). Sin servidor: la extensión habla por
  **mensajería nativa** con `CredentialsHost.exe`, que pasa cada petición a la aplicación por una
  tubería con nombre solo accesible por el mismo usuario (`Platforms/Windows/ExtensionBridge.cs`);
  si la aplicación está cerrada, la arranca en la bandeja, y si la bóveda está bloqueada, trae la
  ventana y pide desbloquear. La aplicación la instala desde **Ajustes › Extensiones del navegador**
  (o lo ofrece tras desbloquear): registra el host en `HKCU`, deja la extensión desempaquetada en
  `%LOCALAPPDATA%\sOCCredentials\extension` y abre el navegador en su página de extensiones para
  cargarla («Cargar desempaquetada»); en cuanto conecta, sale como instalada. Firefox solo admite
  extensiones firmadas por Mozilla: hasta publicarla, se carga temporal desde `about:debugging`.
  El MSIX de la Store no puede registrar el host (virtualización del registro): las extensiones
  necesitan la versión exe.
- En Android la ventana va con `FLAG_SECURE` (sin capturas ni miniatura en recientes).
- Fichas de las tiendas en `store/google-play/` y `store/microsoft/` (espejo en
  `Mobile/GooglePlayConsole/Credentials/` y `Mobile/MicrosoftStore/Credentials/`).

## Estructura (constitución 5, 7 y anexo A.1)

- `Pages/`: `UnlockPage` (crear/desbloquear), `VaultPage` (lista, buscador, filtros por favoritas,
  clase, carpeta y etiqueta), `EntryPage` (detalle/edición), `ScanPage` (QR), `SettingsPage`,
  `AboutPage`. Code-behind delgado, sin ViewModels.
- `Services/`: `VaultCrypto`, `VaultStore` (fichero local, clave, sincronización), `Cloud`
  (OAuth PKCE sin bibliotecas, Google Drive, OneDrive), `Totp` (+ `PasswordGenerator`),
  `Importers`, `OAuthSecrets`, localización y ajustes.
- `Platforms/Android/` y `Platforms/Windows/`: biometría (`BiometricPrompt` / `UserConsentVerifier`),
  navegador OAuth (WebAuthenticator con esquema propio / servidor local en 127.0.0.1) y toast.
- `Launcher/`: el exe de un solo fichero para Windows (WinUI no admite el single-file de .NET); lo
  genera `tools\publicar-windows.ps1 -Msix` junto con el zip y el MSIX.
- `Host/`: `CredentialsHost.exe`, el host de mensajería nativa (un solo exe, sin ventana) que va
  dentro de la carpeta de la aplicación. `Extension/`: la extensión (Manifest V3; `common/` +
  `chromium/manifest.json` con la clave fija, id `hbimfdiggibkbjnmkagdcnddpghhckho`, y
  `firefox/manifest.json`, id `credentials@socratic.app`); se copia tal cual a la salida Windows.
- `Services/AutofillLogic.cs`: lo común al autocompletar de Android y a las extensiones (qué
  entradas casan con un dominio o una app; cómo se guarda lo que el usuario acaba de escribir).
- Los identificadores OAuth van en `oauth.local.props` (ignorado; ver `oauth.local.props.example`)
  y llegan al binario como `AssemblyMetadata`; sin ellos, el proveedor no se ofrece.

## Compilar

```powershell
dotnet build -f net10.0-android36.0 -c Debug          # Android
dotnet build -f net10.0-windows10.0.19041.0 -c Debug  # Windows
.\tools\publicar-windows.ps1 -Msix                     # exe de un solo fichero + zip + MSIX
```

En Debug, `Credentials.exe --master <clave> --demo` crea o abre la bóveda con esa clave y siembra
entradas inventadas (solo para probar y capturar pantallas; no existe en Release). Con la variable
`SOC_SANDBOX=<carpeta>` la bóveda y los ajustes van a esa carpeta y no se tocan los del usuario.
El host de las extensiones se compila aparte (`dotnet publish Host\Host.csproj -c Release`) y el
proyecto lo copia a la salida si existe. `tools\empaquetar-extension.ps1` deja en `bin\extension\`
los zips para Chrome Web Store / Edge Add-ons (chromium) y AMO (firefox). En Android (Debug), los
mismos ganchos van como extras del intent: `am start … --es master <clave> --ez demo true --es lang es
--es page settings`; y en Debug no hay `FLAG_SECURE`, para poder capturar.

## Licencia

MIT. Terceros: ZXing.Net.Maui (Apache 2.0 / MIT), Konscious.Security.Cryptography.Argon2 (MIT),
AndroidX.Biometric (Apache 2.0).
