# Changelog — sOC Credentials

Todas las versiones siguen el esquema de fecha `AAAA.MM.DD.NN` (constitución 11).

## 2026.09.19.00 — Primera versión

`versionCode`: 2026091900 · Windows `2026.9.19.0`

- **Bóveda cifrada** con contraseña maestra (Argon2id + AES-256-GCM), en el aparato o en la
  carpeta privada de la aplicación de tu Google Drive u OneDrive, con mezcla por entrada entre
  aparatos. Desbloqueo con Windows Hello / huella (clave en la bóveda del sistema), bloqueo por
  inactividad y vaciado del portapapeles.
- Entradas de sitio web, aplicación, código de segundo factor y nota segura: usuario, contraseña
  con fortaleza e historial, URL, carpeta, etiquetas, favorita, notas y campos extra.
- **Segundo factor** (TOTP/HOTP) pegando el enlace, la clave o escaneando el QR; código vivo con
  cuenta atrás. **Generador** de contraseñas.
- **Importar** de Chrome, Edge, Brave, Opera, Vivaldi, Firefox, Safari, Bitwarden, KeePass,
  KeePassXC, Aegis, 2FAS y el QR de Google Authenticator. Exportar cifrado o en JSON.
- Android y Windows con el mismo proyecto; en Windows, un solo exe (lanzador) y MSIX.
