$ErrorActionPreference = 'Stop'

Write-Host "==> 1. Terminating running processes..." -ForegroundColor Cyan
Get-Process -Name "iCloudPassFlow", "iCloud-PassFlow" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

Write-Host "==> 2. Cleaning previous build artifacts..." -ForegroundColor Cyan
Remove-Item -Path "build", "dist", "*.spec" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path "dist\staging" -Force | Out-Null

Write-Host "==> 3. Compiling Python app with PyInstaller (external lib)..." -ForegroundColor Cyan
python -m PyInstaller --noconsole --name iCloudPassFlow --icon icon.ico --contents-directory lib --clean -y app.py

if (!(Test-Path "dist\iCloudPassFlow\iCloudPassFlow.exe")) {
    Write-Error "PyInstaller failed: iCloudPassFlow.exe not found."
}

Move-Item -Path "dist\iCloudPassFlow\*" -Destination "dist\staging\" -Force
Remove-Item -Path "dist\iCloudPassFlow" -Recurse -Force

Write-Host "==> 4. Compiling uninstaller (uninstall.exe)..." -ForegroundColor Cyan
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /target:winexe /win32icon:icon.ico /optimize+ /platform:anycpu /r:Microsoft.CSharp.dll /out:dist\staging\uninstall.exe uninstall.cs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to compile uninstall.cs"
}

Write-Host "==> 5. Creating compressed payload archive..." -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stagingDir = Join-Path (Get-Location) "dist\staging"
$payloadZip = Join-Path (Get-Location) "dist\payload.zip"

for ($i = 0; $i -lt 5; $i++) {
    try {
        if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
        [System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
        break
    } catch {
        if ($i -eq 4) { throw }
        Start-Sleep -Milliseconds 500
    }
}

Write-Host "==> 6. Compiling standalone setup installer (iCloudPassFlow-Setup.exe)..." -ForegroundColor Cyan
& $csc /target:winexe /win32icon:icon.ico /optimize+ /platform:anycpu `
    /r:Microsoft.CSharp.dll `
    /r:System.Windows.Forms.dll `
    /r:System.Drawing.dll `
    /r:System.IO.Compression.dll `
    /r:System.IO.Compression.FileSystem.dll `
    /r:System.Management.dll `
    /resource:dist\payload.zip,Payload `
    /out:dist\iCloudPassFlow-Setup.exe `
    setup.cs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to compile setup.cs"
}

Write-Host "==> 7. Cleaning temporary staging files..." -ForegroundColor Cyan
Remove-Item -Path "dist\staging", "dist\payload.zip", "build", "*.spec" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "======================================================" -ForegroundColor Green
Write-Host "   BUILD SUCCESSFUL!                                  " -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Green
Get-ChildItem -Path "dist" | Format-Table Name, Length, LastWriteTime
