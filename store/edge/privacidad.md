# Ficha de Edge Add-ons (Partner Center) — sOC Credentials

Para la pestaña **Privacidad** de la extensión en https://partner.microsoft.com/dashboard/microsoftedge.
Paquete: `C:\ID\OneDrive\Credentials\extension\sOCCredentials-extension-chromium-2026.9.24.2.zip`.
Cada texto cabe de sobra en los 1000 caracteres del campo.

## Descripción del propósito único

```
Rellenar usuarios, contraseñas y códigos de segundo factor (TOTP) en las webs desde la bóveda de la aplicación sOC Credentials instalada en el PC, y guardar en ella las credenciales nuevas que el usuario escriba. La extensión es solo el puente entre el navegador y esa aplicación local: no guarda datos ni se conecta a ningún servidor.
```

## Justificación de permisos

### nativeMessaging

```
Es la única vía por la que la extensión obtiene las credenciales: pide a la aplicación sOC Credentials instalada en el mismo PC (host de mensajería nativa "com.socratic.credentials") las entradas de la web actual, los códigos TOTP y guarda las nuevas. Sin este permiso la extensión no puede cumplir su propósito, porque la bóveda cifrada vive en esa aplicación y no en el navegador.
```

### activeTab

```
Para actuar sobre la pestaña que el usuario está usando cuando abre el popup o pulsa «Rellenar»: saber de qué web se trata (para buscar sus entradas) y rellenar los campos de usuario y contraseña de esa pestaña.
```

### tabs

```
Para leer la URL de la pestaña activa y así mostrar en el popup y en el icono solo las entradas que corresponden a ese sitio, y para enviar a esa pestaña la orden de rellenar cuando el usuario elige una entrada. No se guarda ni se envía el historial de navegación.
```

### storage

```
Para recordar, en el propio navegador, las preferencias del usuario en el popup (por ejemplo, si ya respondió a la pregunta de desactivar el gestor de contraseñas del navegador). No se guardan contraseñas ni datos de las webs.
```

### scripting

```
Para rellenar los campos de usuario, contraseña y código de segundo factor en la página cuando el usuario elige una entrada desde el popup o el menú contextual, en pestañas donde el script de contenido aún no estaba cargado.
```

### contextMenus

```
Añade la opción «Rellenar con sOC Credentials» al menú del botón derecho sobre campos de texto, para rellenar ese campo con la entrada elegida.
```

### clipboardWrite

```
Para copiar al portapapeles el usuario, la contraseña o el código de segundo factor cuando el usuario pulsa el botón de copiar en el popup (por ejemplo, en webs donde el relleno automático no funciona). Solo copia cuando el usuario lo pide.
```

### privacy

```
Solo para una opción que el usuario activa expresamente en el popup: desactivar el guardado de contraseñas propio del navegador (privacy.services.passwordSavingEnabled), para que Edge y sOC Credentials no ofrezcan a la vez guardar la misma contraseña. No se lee ni se cambia ningún otro ajuste de privacidad.
```

### Permisos de host

```
El script de contenido tiene que ejecutarse en cualquier web (<all_urls>) porque cualquier web puede tener un formulario de inicio de sesión: detecta los campos de usuario y contraseña para ofrecer, junto al campo, las entradas de la bóveda que corresponden a ese sitio, y para ofrecer guardar lo que el usuario escribe al iniciar sesión. Lo que lee de la página no sale del PC: solo se comunica con la aplicación local.
```

## ¿Usa código remoto?

**No.**

Justificación (si la pide):

```
Todo el JavaScript va dentro del paquete (background.js, content.js, popup.js). No hay etiquetas <script> externas, ni módulos remotos, ni eval() ni new Function(), y la extensión no hace peticiones de red: solo se comunica con la aplicación local por mensajería nativa.
```

## Uso de datos — ¿qué datos de usuario recopila?

**No marques ninguna casilla.** «Recopilar» en estas tiendas quiere decir sacar datos del dispositivo
del usuario hacia el desarrollador o terceros, y la extensión no lo hace: las credenciales van de la
aplicación de tu PC a la página y de vuelta, sin salir del equipo (lo he comprobado en el código: no
hay ni una petición de red).

Si el revisor la rechazara por eso (un gestor de contraseñas maneja, al fin y al cabo, «Información
de autenticación» y lee el «Contenido del sitio web»), la alternativa es marcar esas dos y dejar
claro en la política que se procesan solo en el dispositivo.

## URL de la directiva de privacidad

```
https://github.com/donki/Credentials/blob/master/PRIVACY.md
```

## Certificaciones

Marca **las tres**: no se venden ni transfieren datos a terceros, no se usan para nada ajeno al
propósito único, y no se usan para decidir solvencia ni para préstamos. Las tres son ciertas.
