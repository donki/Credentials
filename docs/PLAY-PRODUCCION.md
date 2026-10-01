# Solicitud de acceso a producción en Google Play — Credentials (sOC Credentials)

Respuestas para el cuestionario de Play Console › **Panel › Solicitar acceso a producción**, en
catalán (el idioma de la consola). Cada texto cabe en los 300 caracteres del formulario; el número
entre paréntesis es su longitud. Constitución Mobile §11. **Última actualización: 2026-10-01**
(versión 2026.10.01.00). Estado en Play: prueba cerrada recién dada de alta (alpha 2026.09.28.04; grupos desde el 2026-09-29).

> Lo marcado con ⚠ no lo puedo saber yo: compruébalo en la consola antes de enviarlo y cámbialo si
> no es así.
>
> - ⚠ La API ya da la alpha 2026.09.28.04 como «completed» (09-PENDIENTE decía borrador): comprueba en la consola que está en revisión o publicada. **Los 14 días no se cumplen antes de mediados de octubre**: hasta entonces no se puede pedir producción.
> - ⚠ Faltaban en la consola (sin API): política de privacidad, Seguridad de los datos, la declaración del servicio de autocompletar / gestor de contraseñas, clasificación y público.
> - ⚠ En alpha está la 2026.09.28.04; los arreglos de abajo son de la 2026.09.30.00 en adelante: súbeme la última antes de pedir producción.

---

## Informació sobre la prova tancada

**Com has reclutat usuaris per a la prova tancada?** (273) ⚠ *el autocompletar se probó en el emulador; comprueba que la usas en el Xiaomi.*

```
He afegit a la prova tancada quatre grups públics de Google de verificadors voluntaris (comunitats d'intercanvi de proves de 12 persones durant 14 dies). No he fet servir cap proveïdor de pagament. També l'he fet servir cada dia en un mòbil real amb Android 16 i a Windows.
```

**Fins a quin punt t'ha resultat fàcil reclutar verificadors?** — Propuesta: **Ni fàcil ni difícil** (los grupos públicos dan el número, pero participan poco).

**Descriu la implicació dels verificadors** (265) ⚠ *comprueba en Estadísticas / Prova tancada que de verdad la abrieron; si no hay datos, quita la parte de las funciones.*

```
Els verificadors han instal·lat l'app, han creat una bóveda amb contrasenya mestra i hi han desat entrades i codis de segon factor. Un usuari real hi importaria totes les contrasenyes i usaria l'emplenament automàtic cada dia, cosa que amb dades de prova no es veu.
```

**Resum dels suggeriments i com els has recollit** (256) ⚠ *si algún verificador dejó comentarios (en la consola o por correo), menciónalos.*

```
Pocs comentaris escrits dels verificadors; els he recollit des de la consola de Play i GitHub. Les millores han sortit del meu ús diari al mòbil i a Windows i del banc de proves: textos en anglès, bóvedes amb salts de línia de Windows i duplicats en desar.
```

## Informació sobre l'aplicació

**A quin públic objectiu va dirigida?** (227)

```
Persones que volen guardar contrasenyes i codis de segon factor de forma segura sense dependre d'un servidor d'un tercer: la bóveda xifrada es queda al mòbil o, si volen, al seu Google Drive o OneDrive. Sense anuncis ni compte.
```

**Com proporciona valor als usuaris?** (254)

```
Desa contrasenyes, codis de segon factor i notes en una bóveda xifrada amb la contrasenya mestra (Argon2id i AES-256-GCM), s'obre amb l'empremta, emplena a apps i navegadors i importa de Chrome, Edge, Firefox i Google Authenticator. Sense servidor propi.
```

**Instal·lacions esperades el primer any** — Propuesta: **0 - 10.000** (app nueva, sin promoción).

## Preparació per a la producció

**Quins canvis has fet en funció de la prova tancada?** (250) ⚠ *la prueba cerrada acaba de empezar: si los verificadores informan de algo, añádelo aquí.*

```
He corregit tres errors que va trobar el banc de 151 proves automàtiques: dos avisos que sortien en castellà amb l'app en anglès, una bóveda amb salts de línia de Windows que no s'obria i una entrada que es duplicava en desar-la des de l'emplenament.
```

**Com has decidit que està preparada per a producció?** (282) ⚠ *comprueba en Qualitat › Android Vitals que no hay fallos; si los hay, quita «sense tancaments a la consola». Antes, que estén enviadas la Seguridad de los datos y la declaración de autocompletar.*

```
Les 151 proves automàtiques passen totes, l'he provada en un mòbil real amb Android 16 sense errors, els verificadors l'han fet servir 14 dies sense tancaments a la consola i la fitxa, la privadesa i la seguretat de les dades i la declaració d'emplenament automàtic estan completes.
```
