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
- Los identificadores OAuth van en `oauth.local.props` (ignorado; ver `oauth.local.props.example`)
  y llegan al binario como `AssemblyMetadata`; sin ellos, el proveedor no se ofrece.

## Compilar

```powershell
dotnet build -f net10.0-android36.0 -c Debug          # Android
dotnet build -f net10.0-windows10.0.19041.0 -c Debug  # Windows
.\tools\publicar-windows.ps1 -Msix                     # exe de un solo fichero + zip + MSIX
```

En Debug, `Credentials.exe --master <clave> --demo` crea o abre la bóveda con esa clave y siembra
entradas inventadas (solo para probar y capturar pantallas; no existe en Release).

## Licencia

MIT. Terceros: ZXing.Net.Maui (Apache 2.0 / MIT), Konscious.Security.Cryptography.Argon2 (MIT),
AndroidX.Biometric (Apache 2.0).
