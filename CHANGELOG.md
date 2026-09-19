# Changelog — sOC Credentials

Todas las versiones siguen el esquema de fecha `AAAA.MM.DD.NN` (constitución 11).

## 2026.09.19.10 — Android: guardar lo que escribes en apps y navegadores

`versionCode`: 2026091910 · Windows `2026.9.19.10`

- **Autocompletar: guardar.** Al enviar un formulario de usuario y contraseña en una app o en el
  navegador (con el servicio de autocompletar de Credentials activo), Android pregunta «¿Guardar la
  contraseña en Credentials?». Si aceptas, se guarda en la bóveda: entrada nueva con el dominio o
  el nombre de la app como título, o, si ya existía esa cuenta, se le cambia la contraseña (la
  anterior queda en el historial). Con la bóveda bloqueada pide desbloquear antes.

## 2026.09.19.09 — El logo de Google no cambia al conectar

`versionCode`: 2026091909 · Windows `2026.9.19.9`

- En Ajustes, el botón de Google Drive enseñaba el logo en blanco al estar conectado; ahora es
  siempre el logo en color, esté conectado o no.

## 2026.09.19.08 — Correcciones al entrar con OneDrive

`versionCode`: 2026091908 · Windows `2026.9.19.8`

- **Android**: la vuelta del navegador tras entrar con Microsoft no llegaba a la aplicación (el
  filtro `com.socratic.credentials://auth` se perdía al fusionar el manifiesto); ahora los dos
  filtros, el de Microsoft y el de Google, están declarados en el manifiesto.
- **Windows**: si la entrada tardaba más de la cuenta salía «Cannot access a disposed object
  (HttpListener)»; ahora hay diez minutos, el aviso es claro («La entrada no se completó a tiempo»)
  y el servidor local ignora peticiones del navegador que no son la vuelta (favicon, etc.).

## 2026.09.19.07 — Google con proyecto propio

`versionCode`: 2026091907 · Windows `2026.9.19.7`

- El cliente de Google es ahora del proyecto propio «sOC Credentials»: la pantalla de
  consentimiento enseña el nombre de la aplicación (antes salía el de RCManager, porque el
  cliente estaba en su proyecto).

## 2026.09.19.06 — OneDrive activado

`versionCode`: 2026091906 · Windows `2026.9.19.6`

- Compilada con el registro propio de Entra ID («sOC Credentials», multiinquilino y cuentas
  personales): la opción **OneDrive** de Ajustes ya entra con la cuenta y sincroniza la bóveda en la
  carpeta de la aplicación. Con esto los tres almacenes están operativos.

## 2026.09.19.05 — Cliente de Google correcto

`versionCode`: 2026091905 · Windows `2026.9.19.5`

- La 2026.09.19.04 llevaba un cliente OAuth de Google de tipo equivocado; esta lleva el de
  «Aplicación de escritorio». Si la anterior no dejaba entrar en Google Drive, esta sí.

## 2026.09.19.04 — Google Drive activado

`versionCode`: 2026091904 · Windows `2026.9.19.4`

- Compilada con el cliente OAuth propio de Google: la opción **Google Drive** de Ajustes ya entra
  con la cuenta y sincroniza la bóveda en la carpeta privada de la aplicación. OneDrive sigue
  pendiente del registro de Entra.

## 2026.09.19.03 — Ajustes: idioma, almacenamiento con botones, bandeja y arranque con Windows

`versionCode`: 2026091903 · Windows `2026.9.19.3`

- **Dónde vive la bóveda** ahora son tres botones (solo en este aparato, Google Drive, OneDrive),
  con el elegido resaltado, como los de entrar en Task Manager.
- El **idioma** se cambia en Ajustes (antes en «Acerca de»).
- **Windows**: al minimizar se queda en el área de notificación (clic para volver; botón derecho,
  Abrir o Salir) y hay interruptor para **arrancar con Windows** escondida en la bandeja. Los dos
  en la tarjeta «Windows» de Ajustes.
- En **Android** la aplicación se llama «Credentials» (icono, ajustes del sistema, autocompletar).
- Al bloquearse la bóveda (botón o inactividad) la lista se vacía y sale el desbloqueo en el acto.

## 2026.09.19.02 — Autocompletar en Android, fichas y registro de Entra

`versionCode`: 2026091902 · Windows `2026.9.19.2`

- **Autocompletar de Android**: servicio de autocompletar del sistema que, en apps y navegadores,
  propone las credenciales que casan con el sitio o la aplicación; si la bóveda está bloqueada,
  pide desbloquear (huella o contraseña) y rellena después.
- Fichas de Google Play y Microsoft Store (textos, logos, capturas provisionales) y el script
  `tools\Registrar-Entra.ps1` para dar de alta el cliente de Microsoft.

## 2026.09.19.01 — Anclaje a la barra de tareas

`versionCode`: 2026091901 · Windows `2026.9.19.1`

- La ventana lleva identidad y comando de relanzamiento hacia `sOCCredentials.exe`: se puede
  anclar a la barra de tareas y el anclaje sobrevive a las actualizaciones.

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
