<#
.SYNOPSIS
    Empaqueta la extension de navegador para subirla a las tiendas (Chrome Web Store, Edge Add-ons, AMO).
.DESCRIPTION
    Junta Extension\common con el manifiesto de cada navegador y deja dos zips en bin\extension\:
    sOCCredentials-extension-chromium-<version>.zip (Chrome y Edge, mismo paquete) y
    sOCCredentials-extension-firefox-<version>.zip (AMO lo firma y devuelve un .xpi). La version es
    la del manifiesto. Los zips llevan los ficheros en la raiz, como piden las tres tiendas.
.EXAMPLE
    .\tools\empaquetar-extension.ps1
#>
[CmdletBinding()]
param()
$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot
$ext = Join-Path $raiz "Extension"
$salida = Join-Path $raiz "bin\extension"
New-Item -ItemType Directory -Force $salida | Out-Null
$version = (Get-Content (Join-Path $ext "chromium\manifest.json") -Raw | ConvertFrom-Json).version
foreach ($nav in "chromium", "firefox") {
    $trabajo = Join-Path $salida $nav
    if (Test-Path $trabajo) { Remove-Item $trabajo -Recurse -Force }
    New-Item -ItemType Directory -Force $trabajo | Out-Null
    Copy-Item (Join-Path $ext "common\*") $trabajo -Recurse -Force
    Copy-Item (Join-Path $ext "$nav\manifest.json") $trabajo -Force
    $zip = Join-Path $salida "sOCCredentials-extension-$nav-$version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $trabajo "*") -DestinationPath $zip -CompressionLevel Optimal
    Remove-Item $trabajo -Recurse -Force
    "$zip ($([math]::Round((Get-Item $zip).Length/1KB)) KB)"
}
