# Prueba de la detección de campos de acceso de la extensión (content.js), en Edge sin ventana.
# Cada casilla de la página se enfoca con una API de extensión simulada que siempre tiene una entrada:
# donde la extensión cree que es un acceso, sale la lista («LISTA»). Casos: un formulario tipo ERP,
# los destinatarios de alertas de un portal, un correo suelto con «Save», un acceso en dos pasos tipo
# Microsoft, otro con autocomplete=username y un acceso clásico con contraseña.
$ErrorActionPreference = 'Stop'
$pagina = Join-Path $PSScriptRoot 'deteccion.html'
$esperado = 'bc_no=no bc_name=no bc_addr=no bc_city=no bc_contact=no bc_userid=no bc_email=no bc_phone=no az1=no az2=no az3=no ms_user=LISTA ts_user=LISTA lg_user=LISTA lg_pass=LISTA'
$edge = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe').'(default)'
$salida = & $edge --headless=new --disable-gpu --allow-file-access-from-files --virtual-time-budget=15000 --dump-dom ("file:///" + ($pagina -replace '\\', '/')) 2>$null | Select-String 'RESULT (.*)</pre>'
$obtenido = $salida.Matches[0].Groups[1].Value.Trim()
if ($obtenido -ne $esperado) { Write-Host "FALLA`n esperado: $esperado`n obtenido: $obtenido" -ForegroundColor Red; exit 1 }
Write-Host "Detección de campos: 15 de 15 casos bien" -ForegroundColor Green
