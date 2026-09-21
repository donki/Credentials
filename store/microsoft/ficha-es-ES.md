# Descripción de Store — Español (España)

Todo lo de aquí es para pegar tal cual en el formulario de Partner Center.

Antes de enviar: **reservar el nombre** «sOC Credentials» en Partner Center y comprobar que la
identidad del paquete (`Platforms\Windows\Package.appxmanifest`, `Identity Name` =
`sOCratic.sOCCredentials`) coincide con la que asigne *Product management › Product identity*. El
paquete lo genera `tools\publicar-windows.ps1 -Msix` (sale en `bin\windows\`).

---

## Nombre del producto

```
sOC Credentials
```

## Descripción

```
sOC Credentials guarda tus contraseñas y tus códigos de segundo factor en una bóveda cifrada con una
contraseña maestra que solo tú conoces. Sin servidor nuestro, sin cuenta nuestra y sin analítica: la
bóveda vive en tu PC o, si tú lo eliges, en la carpeta privada de la aplicación de tu propio Google
Drive u OneDrive.

CÓMO SE PROTEGE
• La clave sale de tu contraseña maestra con Argon2id y todo se cifra con AES-256-GCM en tu propio
  equipo. Lo que sube a la nube ya va cifrado; nadie —nosotros tampoco— puede leerlo ni recuperar
  la contraseña.
• Desbloqueo con Windows Hello: la clave queda protegida por Windows (DPAPI) y solo se libera cuando
  el sistema te verifica.
• Bloqueo automático por inactividad y vaciado del portapapeles a los segundos que elijas.

QUÉ GUARDA
• Sitios web y aplicaciones: usuario, contraseña (con medidor de fortaleza e historial), dirección,
  carpeta, etiquetas, favoritas, notas y campos extra ocultables.
• Códigos de segundo factor (TOTP/HOTP), como una app de autenticación: pega el enlace o la clave, o
  escanea el QR con la cámara; el código se ve con su cuenta atrás y se copia con un clic.
• Notas seguras.

EN TODOS TUS DISPOSITIVOS
• Elige la misma cuenta de Google o Microsoft en el PC y en el móvil (hay aplicación para Android) y
  usa la misma contraseña maestra: los cambios se mezclan entrada a entrada, gana la más reciente.

TRAE LO QUE YA TIENES
• Importa el CSV de Chrome, Edge, Brave, Firefox o Safari, Bitwarden, KeePass y KeePassXC; el JSON
  de Aegis o 2FAS; y las cuentas de Google Authenticator con su QR de exportación.
• Exporta la bóveda cifrada cuando quieras.

Generador de contraseñas a tu gusto. Software libre bajo licencia MIT. En español y en inglés, con
modo claro y oscuro.
```

## Novedades de esta versión

Se deja **en blanco** en el primer envío.

## Características del producto

```
Bóveda cifrada con contraseña maestra (Argon2id + AES-256-GCM)
En tu PC o en la carpeta privada de tu Google Drive u OneDrive
Desbloqueo con Windows Hello, bloqueo por inactividad
Contraseñas, aplicaciones, notas seguras y códigos 2FA (TOTP)
QR de segundo factor con la cámara; código con cuenta atrás
Generador de contraseñas y medidor de fortaleza
Importa de Chrome, Edge, Firefox, Bitwarden, KeePass, Aegis, 2FAS, Google Authenticator
La misma bóveda en Android
Software libre, en español y en inglés, claro y oscuro
```

## Palabras clave

```
contraseñas, gestor de contraseñas, 2fa, totp, autenticador, bóveda, cifrado, credenciales
```

## Categoría

Seguridad (o Utilidades y herramientas).

## Capturas de pantalla

En `capturas/` (1366×900, con datos inventados: compilación Debug con `--master … --demo`).

| Fichero | Qué se ve |
|---|---|
| `01-boveda.png` | La bóveda: buscador, filtros y entradas con códigos 2FA vivos |
| `02-detalle.png` | Una entrada: contraseña con fortaleza, generador, segundo factor, carpeta y etiquetas |
| `03-ajustes.png` | Ajustes: dónde vive la bóveda, seguridad, importar y exportar |

## Logotipos

En `logos/`: póster 9:16 (720×1080 y 1440×2160), caja 1:1 (1080 y 2160), icono 300/150/71 y
superhéroe 16:9 (1920×1080 y 3840×2160).

## Dependencias de software (política 10.2.4.1)

No hay software no integrado que declarar: la aplicación lleva .NET y WinUI dentro. Google Drive y
OneDrive son opcionales y son cuentas del propio usuario.

## Declaración de privacidad (para el campo «URL de la política de privacidad»)

Vale la misma página de política del catálogo (ver `constitution/CONSTITUCION-WEB.md`), con el
párrafo específico: «sOC Credentials no recoge ningún dato. La bóveda se guarda cifrada en el equipo
o, si el usuario lo elige, en la carpeta privada de la aplicación de su propio Google Drive u
OneDrive; la aplicación no tiene servidor propio ni envía nada a terceros.»
