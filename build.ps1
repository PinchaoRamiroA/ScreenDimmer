$ErrorActionPreference = "Stop"

$dotnetExe = "$env:USERPROFILE\.dotnet\dotnet.exe"
if (!(Test-Path $dotnetExe)) {
    $found = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($found) {
        $dotnetExe = $found.Source
    } else {
        Write-Error ".NET SDK no encontrado en $dotnetExe ni en PATH."
        exit 1
    }
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Compilando ScreenDimmer con $($dotnetExe)" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

& $dotnetExe --version

Write-Host "`n[1/3] Restaurando paquetes y dependencias..." -ForegroundColor Yellow
& $dotnetExe restore

Write-Host "`n[2/3] Compilando solución en modo Release..." -ForegroundColor Yellow
& $dotnetExe build -c Release --no-restore

Write-Host "`n[3/3] Publicando ejecutable optimizado (win-x64)..." -ForegroundColor Yellow

if (Test-Path "publish\ScreenDimmer.exe") {
    try {
        $f = [System.IO.File]::Open("publish\ScreenDimmer.exe", [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $f.Dispose()
    } catch {
        $bak = "publish\ScreenDimmer_old_" + (Get-Random) + ".bak"
        Move-Item "publish\ScreenDimmer.exe" $bak -Force -ErrorAction SilentlyContinue
    }
}

& $dotnetExe publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o publish\

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host "Compilación y publicación completadas con éxito." -ForegroundColor Green
Write-Host "Ejecutable generado en: publish\ScreenDimmer.exe" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
