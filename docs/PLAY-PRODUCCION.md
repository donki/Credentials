# Solicitud de acceso a producción en Google Play — Credentials

**Enviada el 2026-11-16 a las 12:40** desde Play Console › Panel (Google dice que suele tardar 7 días o menos y avisa por correo al propietario).
Versión en la prueba cerrada: **2026.11.02.00** (alpha). Verificadores: TestCircle (grupo #105, ciclo cerrado). Pruebas automáticas: 284, todas correctas ese día.
Sin bloqueos ni ANR en 28 días. 6 comentarios en la consola, todos de 5 estrellas y sin fallos. La 2026.11.14.00 (lo que pidieron) no está subida.

Respuestas enviadas, en catalán (el idioma de la consola), tal cual:

## Informació sobre la prova tancada

**Com has reclutat usuaris per a la prova tancada?** (252)

```
Amb un grup gratuït d'intercanvi de proves: uns quants desenvolupadors provem les apps dels altres per etapes durant 14 dies i deixem informes amb captures. No he fet servir cap proveïdor de pagament. També l'he provada en un mòbil real amb Android 16.
```

**Fins a quin punt t'ha resultat fàcil reclutar verificadors?** — **Difícil**.

**Descriu la implicació dels verificadors** (252)

```
Els verificadors han creat la bóveda, hi han desat i cercat entrades i han revisat els ajustos (empremta, importació, emplenament). Un usuari real també la sincronitzaria amb el seu Drive o OneDrive i la faria servir cada dia, cosa que pocs han provat.
```

**Resum dels suggeriments i com els has recollit** (248)

```
Recollits als informes de cada etapa del grup i a la consola de Play: cap tancament ni error greu. Han demanat poder fer captures, el botó de bloqueig a dalt a la dreta, confirmar abans d'esborrar la bóveda i veure un tros de les notes a la llista.
```

## Informació sobre l'aplicació

**A quin públic objectiu va dirigida?** (227)

```
Persones que volen guardar contrasenyes i codis de segon factor de forma segura sense dependre d'un servidor d'un tercer: la bóveda xifrada es queda al mòbil o, si volen, al seu Google Drive o OneDrive. Sense anuncis ni compte.
```

**Com proporciona valor als usuaris?** (240)

```
Desa contrasenyes, codis de segon factor i notes en una bóveda xifrada amb la contrasenya mestra (Argon2id i AES-256-GCM), s'obre amb l'empremta, emplena a apps i navegadors i importa dels navegadors i d'apps de codis. Sense servidor propi.
```

**Instal·lacions esperades el primer any** — **0 - 10.000**.

## Preparació per a la producció

**Quins canvis has fet en funció de la prova tancada?** (244)

```
Un interruptor per permetre captures de pantalla i un paquet de 32 bits perquè un verificador no la podia baixar. Ja tinc a punt la resta: bloqueig a dalt a la dreta, confirmació en esborrar, tros de les notes a la llista i ajuda de l'empremta.
```

**Com has decidit que està preparada per a producció?** (241)

```
Les 284 proves automàtiques passen totes, l'he provada en un mòbil real amb Android 16 sense errors, a la consola no hi ha cap bloqueig ni ANR en 28 dies i els verificadors la valoren bé. La fitxa i la seguretat de les dades estan completes.
```
