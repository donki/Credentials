# Changelog — sOC Credentials

Todas las versiones siguen el esquema de fecha `AAAA.MM.DD.NN` (constitución 11).

## 2026.09.23.04 — Arreglo: Firefox decía «desconectado» tras cada versión nueva

`versionCode`: 2026092304 · Windows `2026.9.23.4` · extensión `2026.9.23.4`

- **El puente apuntaba a la carpeta de la versión.** El lanzador desempaqueta cada versión en
  `app\<versión>` y **borra la anterior**, así que el manifiesto que el navegador lee se quedaba
  señalando un `CredentialsHost.exe` que ya no existía: Firefox no distingue «no está» de «no
  arranca» y decía **«desconectado»**. Ahora el puente se copia a un sitio fijo
  (`%LOCALAPPDATA%\sOCCredentials\host\CredentialsHost.exe`) y es ahí donde apunta el manifiesto,
  así que sobrevive a las actualizaciones.
- **El puente deja un registro** en `%LOCALAPPDATA%\sOCCredentials\logs\host.log` (arranque,
  errores y cuándo tiene que levantar la aplicación): si algo vuelve a fallar, se ve qué pasó.
- En el popup, «desconectado» ya no sale en crudo: dice que abras sOC Credentials una vez, que es lo
  que hace falta (ella sola registra el puente).

## 2026.09.23.03 — La extensión va con la misma versión que la aplicación

`versionCode`: 2026092303 · Windows `2026.9.23.3` · extensión `2026.9.23.3`

- **La extensión lleva ahora el mismo número que la aplicación.** Iba por su cuenta (se quedó en
  2026.9.22.0) y en OneDrive no había forma de saber si la que había era la de la última entrega.
- En `OneDrive\Credentials\extension` quedan, solo de la versión entregada, los dos zips (Chromium y
  Firefox) y **las dos carpetas desempaquetadas**: la de Chromium se carga por carpeta y la de
  Firefox por su `manifest.json`. Antes solo se desempaquetaba la de Chromium y sobrevivían las
  versiones viejas.
- La que la aplicación deja en `%LOCALAPPDATA%\sOCCredentials\extension` lleva también ese
  número: se pone antes de compilar, así que el manifiesto que va dentro del paquete y el de los
  zips son el mismo (en la 2026.09.23.02 todavía no coincidían).

## 2026.09.23.01 — Arreglo: la extensión de Firefox no podía abrir la aplicación ni conectarse

`versionCode`: 2026092301 · Windows `2026.9.23.1` · extensión `2026.9.22.0`

- **El puente con el navegador se registra solo.** Hasta ahora, el manifiesto del host de mensajería
  nativa y su clave del registro solo se escribían al pulsar «Instalar» en la aplicación; quien
  cargaba la extensión a mano —lo normal en **Firefox**, que solo admite la carga temporal— se
  encontraba con que no podía abrir sOC Credentials ni conectarse con ella. Ahora, al arrancar, la
  aplicación deja la extensión desempaquetada y el host registrado para **todos los navegadores que
  haya en el PC** (Edge, Chrome, Brave, Firefox), y lo rehace en cada versión porque la carpeta
  cambia. Comprobado con el protocolo de Firefox: saludo, consulta y arranque de la aplicación
  estando cerrada.

## 2026.09.23.00 — Arreglo: en Windows la aplicación podía no abrirse

`versionCode`: 2026092300 · Windows `2026.9.23.0` · extensión `2026.9.22.0`

- **Abrir sOC Credentials y que no pasara nada.** Al arrancar, si ya había otra instancia (por
  ejemplo la que levanta el navegador para la extensión, escondida en el área de notificación), esta
  le pedía que se enseñara y se cerraba **sin comprobar que alguien contestara**. Si esa otra estaba
  colgada o era un proceso sin ventana que aún retenía el testigo, no salía ninguna ventana y parecía
  que la aplicación no se ejecutaba. Ahora se espera la confirmación: si no llega en dos segundos,
  arranca esta instancia igual (mejor dos ventanas que ninguna).

## 2026.09.22.01 — Que el gestor de contraseñas del móvil sea sOC Credentials, también en el navegador

`versionCode`: 2026092201 · Windows `2026.9.22.1` · extensión `2026.9.22.0`

- **Ajustes › Autocompletar** explica ahora los tres sitios donde se decide quién rellena las
  contraseñas en Android, y lleva a cada uno:
  - **El servicio de autocompletar** (lo que ya había): manda en las aplicaciones.
  - **El servicio preferido de contraseñas** (Android 14+, Credential Manager): es el que obedecen
    Edge y Chrome. Mientras ahí esté Google, en el navegador sale Google aunque el autocompletar sea
    sOC Credentials. Botón nuevo que abre esa pantalla del sistema.
  - **El gestor propio de cada navegador**: botón nuevo que enseña los pasos («Ajustes › Contraseñas
    → apagar guardar y rellenar») y abre los ajustes del navegador elegido (Edge, Chrome, Brave,
    Firefox, los que estén instalados).

## 2026.09.22.00 — La contraseña se pide una vez por sesión de escritorio; el gestor del navegador se puede volver a encender

`versionCode`: 2026092200 · Windows `2026.9.22.0` · extensión `2026.9.22.0`

- **Ya no pide la contraseña cada vez que se abre el navegador.** Lo hacía porque la extensión
  consulta la bóveda para cada pestaña (la insignia con el número de entradas) y, cerrada, la
  aplicación saltaba a pedir la contraseña. Ahora esas consultas pasivas (insignia, lista pegada al
  campo) solo reciben «bloqueada» (candado 🔒 en el icono); la contraseña se pide cuando el usuario
  actúa: abre el popup, pulsa «Desbloquear» en la lista, el menú contextual o «Guardar».
- **Una vez por sesión de escritorio (Windows)**: con «Arrancar con Windows», al iniciar sesión sale
  la pantalla de desbloqueo (Windows Hello si está activado) y, abierta la bóveda, la ventana se va
  a la bandeja. Al bloquear Windows (Win+L) la bóveda se cierra y se vuelve a pedir al volver a la
  sesión (otra vez con Windows Hello). Cuando la arranca el navegador (`--background`) se queda
  escondida sin pedir nada. El bloqueo por inactividad propio viene ahora apagado en Windows (la
  sesión de Windows ya manda); se puede encender en Ajustes. En Android sigue a 15 minutos.
- **Extensión › Navegador**: interruptor «El navegador guarda y rellena contraseñas» al pie del popup
  para volver a encender el gestor del navegador tras apagarlo (o apagarlo más tarde). Encenderlo
  suelta el ajuste y el navegador recupera el suyo.

## 2026.09.21.00 — Rellenar desde el propio campo, guardar lo escrito y la bóveda abierta mientras usas el PC

`versionCode`: 2026092100 · Windows `2026.9.21.0` · extensión `2026.9.21.0`

- **Lista pegada al campo (web)**: al entrar en el usuario o la contraseña de una página, la
  extensión enseña debajo las entradas del sitio; una pulsación rellena. Si hay algo escrito que no
  está en la bóveda, la primera opción es **«Guardar lo escrito en sOC Credentials»** (también
  como botón en el popup de la extensión). Con la bóveda bloqueada, la lista ofrece desbloquear.
- **Rellenar en las aplicaciones de Windows**: cuando un campo de contraseña de un programa recibe
  el foco, aparece a su lado la lista con las entradas que casan (por el nombre del ejecutable y el
  título de la ventana); al elegir una se teclean el usuario y la contraseña. La entrada aprende el
  programa (campo `windows`) y sale la primera la próxima vez. Los navegadores van por la
  extensión. Interruptor en Ajustes › Autocompletar. La lista no roba el foco y todo el trabajo con
  otros procesos va en un hilo aparte.
- **Un solo gestor de contraseñas**: la extensión propone (una vez) desactivar el guardado de
  contraseñas del navegador; en Android, al desbloquear se propone que sOC Credentials sea el
  servicio de autocompletar si otro gestor lo es (Android solo admite uno), y Ajustes ›
  Autocompletar enseña el estado y permite cambiarlo.
- **La bóveda sigue abierta mientras usas el PC**: la inactividad es la del sistema (teclado y
  ratón, `GetLastInputInfo`), no la de la aplicación; se cierra al bloquear la sesión de Windows
  (Win+L) y, en Android, al apagarse la pantalla. La cuenta atrás corre aunque la ventana esté en
  la bandeja. Ajuste por defecto: 15 min (antes 5).
- **Instancia única en Windows**: si la aplicación ya está abierta (aunque esté en el área de
  notificación), volver a ejecutarla la trae al frente en vez de abrir otra.
- **Borrar desde la lista**: botón de papelera en cada entrada, con confirmación (mismo borrado
  lógico que en la ficha). Los botones de la fila llevan pista (tooltip / pulsación larga).
- «Aparato» pasa a ser «dispositivo» en todos los textos.

## 2026.09.20.03 — Textos de biometría por plataforma

`versionCode`: 2026092003 · Windows `2026.9.20.3`
- En Android ya no se habla de Windows Hello: el botón de la pantalla de desbloqueo y el ajuste
  dicen «Desbloquear con huella o cara»; en Windows, «Desbloquear con Windows Hello».

## 2026.09.20.02 — Icono con el candado más pequeño y splash propio

`versionCode`: 2026092002 · Windows `2026.9.20.2`

- El candado del icono es más pequeño y está centrado (icono adaptativo de Android, icono de
  Windows, tienda y extensión, todos del mismo dibujo).
- La pantalla de arranque de Android enseñaba la papelera de Uninstaller (quedó del proyecto del
  que se partió); ahora es el candado.

## 2026.09.20.01 — Autocompletar de Android probado y con el nombre de la app

`versionCode`: 2026092001 · Windows `2026.9.20.1`

- **Guardar desde el autocompletar**, probado en el móvil de punta a punta: al escribir un usuario y
  contraseña que la bóveda no tiene (o con otra contraseña), Android pregunta «¿Guardar en
  Credentials?»; con la bóveda bloqueada pide desbloquear y guarda; si ya la tiene igual, no
  pregunta; y la entrada nueva sale después como sugerencia para rellenar.
- La entrada nueva lleva el **nombre de la app** («AutofillTest»), no su paquete
  («com.socratic.autofilltest»): el manifiesto declara la visibilidad de las apps con icono.

## 2026.09.20.00 — Extensiones para Edge, Chrome y Firefox

`versionCode`: 2026092000 · Windows `2026.9.20.0`

- **Extensión de navegador** (Windows): en Edge, Chrome y Firefox, el icono de la barra enseña las
  entradas del sitio con rellenar, copiar usuario y contraseña y el código de segundo factor en
  vivo; busca en toda la bóveda; genera contraseñas y las pone en la página; y al enviar un
  formulario nuevo ofrece **guardarlo** (o actualizar la contraseña). Habla con la aplicación del
  PC por mensajería nativa: nada sale del equipo. Con la bóveda bloqueada trae la aplicación y
  pide desbloquear; si está cerrada, la arranca en la bandeja.
- **Instalación desde la aplicación**: tras desbloquear, si algún navegador del PC no tiene la
  extensión, pregunta si instalarla (también en Ajustes › Extensiones del navegador, con el estado
  por navegador y un interruptor para no volver a preguntar). Registra el host y deja la extensión
  desempaquetada; el paso de cargarla en el navegador lo guía paso a paso (los navegadores no
  dejan hacerlo solo hasta que esté publicada en sus tiendas). En Firefox, temporal hasta firmarla.
- La versión de la Store (MSIX) no puede registrar el host: las extensiones necesitan el exe.

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

- **Dónde vive la bóveda** ahora son tres botones (solo en este dispositivo, Google Drive, OneDrive),
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

- **Bóveda cifrada** con contraseña maestra (Argon2id + AES-256-GCM), en el dispositivo o en la
  carpeta privada de la aplicación de tu Google Drive u OneDrive, con mezcla por entrada entre
  dispositivos. Desbloqueo con Windows Hello / huella (clave en la bóveda del sistema), bloqueo por
  inactividad y vaciado del portapapeles.
- Entradas de sitio web, aplicación, código de segundo factor y nota segura: usuario, contraseña
  con fortaleza e historial, URL, carpeta, etiquetas, favorita, notas y campos extra.
- **Segundo factor** (TOTP/HOTP) pegando el enlace, la clave o escaneando el QR; código vivo con
  cuenta atrás. **Generador** de contraseñas.
- **Importar** de Chrome, Edge, Brave, Opera, Vivaldi, Firefox, Safari, Bitwarden, KeePass,
  KeePassXC, Aegis, 2FAS y el QR de Google Authenticator. Exportar cifrado o en JSON.
- Android y Windows con el mismo proyecto; en Windows, un solo exe (lanzador) y MSIX.
