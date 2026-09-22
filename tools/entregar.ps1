# Entrega de sOC Credentials: APK firmado (+ Xiaomi si esta), paquetes Windows, OneDrive, commit,
# push y release. Sin Play todavia (la app no esta dada de alta). Uso: cred_entregar.ps1 2026.09.19.00 "mensaje"
param([string]$Ver, [string]$Mensaje)
$ErrorActionPreference = 'Stop'
Set-Location D:\sOCProjects\Mobile\Credentials
# System.IO no sigue Set-Location: sin esto, las escrituras relativas iban a otra carpeta.
[IO.Directory]::SetCurrentDirectory('D:\sOCProjects\Mobile\Credentials')
$code = $Ver -replace '\.', ''
$win = ($Ver.Split('.') | ForEach-Object { [int]$_ }) -join '.'
$msixVer = "$([int]$Ver.Split('.')[0]).$([int]$Ver.Split('.')[1]).$([int]$Ver.Split('.')[2])$($Ver.Split('.')[3]).0"

$p = 'Credentials.csproj'; $x = Get-Content $p -Raw
$x = [regex]::Replace($x, '<ApplicationDisplayVersion>[^<]*</ApplicationDisplayVersion>', "<ApplicationDisplayVersion>$Ver</ApplicationDisplayVersion>")
$x = [regex]::Replace($x, '<ApplicationVersion>[^<]*</ApplicationVersion>', "<ApplicationVersion>$code</ApplicationVersion>")
$x = [regex]::Replace($x, '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$win</AssemblyVersion>")
$x = [regex]::Replace($x, '<FileVersion>[^<]*</FileVersion>', "<FileVersion>$win</FileVersion>")
[IO.File]::WriteAllText($p, $x)
$m = 'Platforms\Windows\Package.appxmanifest'; $y = Get-Content $m -Raw
$y = [regex]::Replace($y, 'Version="[\d\.]+" />', "Version=`"$msixVer`" />")
[IO.File]::WriteAllText($m, $y)

Get-Process Credentials,sOCCredentials -ErrorAction SilentlyContinue | Stop-Process -Force
$pass = (Get-Content D:\sOCProjects\password.txt -Raw).Trim()

# Android
Get-ChildItem bin\Release\net10.0-android36.0 -Recurse -Filter *.apk -ErrorAction SilentlyContinue | Remove-Item -Force
dotnet publish Credentials.csproj -c Release -f net10.0-android36.0 -p:AndroidPackageFormat=apk "-p:AndroidSigningStorePass=$pass" "-p:AndroidSigningKeyPass=$pass" -nologo -v q 2>&1 | Select-String "error" | Select-Object -First 3
$apk = Get-ChildItem bin\Release\net10.0-android36.0 -Recurse -Filter '*-Signed.apk' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $apk) { throw "sin APK" }
New-Item -ItemType Directory -Force releases | Out-Null
Get-ChildItem releases -Filter 'Credentials-*.apk' | Remove-Item -Force
Copy-Item $apk.FullName "releases\Credentials-$Ver.apk" -Force
# Xiaomi por USB o por Wi-Fi (depuracion inalambrica): el helper devuelve el serial.
$serial = & ..\Shared\xiaomi-conectar.ps1
if ($serial) { "Xiaomi ($serial): " + (adb -s $serial install --no-incremental -r $apk.FullName 2>&1 | Select-Object -Last 1) } else { "Xiaomi: no conectado" }

# Windows
Get-ChildItem bin\windows -ErrorAction SilentlyContinue | Remove-Item -Force
.\tools\publicar-windows.ps1 -Msix 2>&1 | Select-String "Lanzador|MSIX:|error|fallado"
$d = 'C:\ID\OneDrive\Credentials'
New-Item -ItemType Directory -Force $d | Out-Null
Get-ChildItem $d -Include *.msix,*.zip,*.exe -Recurse | Remove-Item -Force
Get-ChildItem bin\windows | Copy-Item -Destination $d -Force
Get-ChildItem releases -Filter 'sOCCredentials-*.zip' | Remove-Item -Force
Copy-Item "bin\windows\sOCCredentials-$win.zip" releases\ -Force
# Extension: zips de las tiendas y la carpeta desempaquetada de Chromium (Josep la carga desde OneDrive).
.\tools\empaquetar-extension.ps1 | Out-Null
$e = Join-Path $d 'extension'; New-Item -ItemType Directory -Force $e | Out-Null
Get-ChildItem bin\extension -Filter *.zip | Copy-Item -Destination $e -Force
$u = Join-Path $e "sOCCredentials-extension-chromium-$win"
if (Test-Path $u) { Remove-Item $u -Recurse -Force }
Expand-Archive "bin\extension\sOCCredentials-extension-chromium-$win.zip" $u

# git + release
git add -A; git commit -q -m $Mensaje; git push -q -u origin HEAD 2>&1 | Select-Object -Last 1
$zip = Get-ChildItem bin\windows -Filter *.zip | Select-Object -First 1
$msix = Get-ChildItem bin\windows -Filter *.msix | Select-Object -First 1
python ..\Shared\release-github.py "v$Ver" "releases\Credentials-$Ver.apk" bin\windows\sOCCredentials.exe $zip.FullName $msix.FullName 2>&1 | Select-Object -Last 1
Get-ChildItem $d | Select-Object Name, Length
