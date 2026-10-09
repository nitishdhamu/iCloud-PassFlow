$ProgressPreference = 'SilentlyContinue'
Write-Host "Installing iCloud-PassFlow..." -ForegroundColor Cyan

$InstallDir = "$env:LOCALAPPDATA\Programs\iCloud-PassFlow"

# 1. Create installation directory
if (!(Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
}

# 2. Stop any existing running instances
Stop-Process -Name "iCloud-PassFlow" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "iCloudPasswords+" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "iCloudPasswordsPlus" -Force -ErrorAction SilentlyContinue

# 3. Fetch latest release package
Write-Host "Fetching latest release from GitHub..."
$repo = "nitishdhamu/iCloud-PassFlow"
$zipPath = "$InstallDir\iCloud-PassFlow.zip"

$downloaded = $false

# Method 1: Direct GitHub Release download (bypasses GitHub API rate limits)
$directUrl = "https://github.com/$repo/releases/latest/download/iCloud-PassFlow.zip"
try {
    Write-Host "Downloading iCloud-PassFlow.zip..."
    Invoke-WebRequest -Uri $directUrl -OutFile $zipPath -ErrorAction Stop
    if ((Test-Path $zipPath) -and (Get-Item $zipPath).Length -gt 1000) {
        $downloaded = $true
    }
} catch {
    # If direct download URL fails, fall through to GitHub API
}

# Method 2: GitHub API lookup
if (!$downloaded) {
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -ErrorAction Stop
        $asset = $release.assets | Where-Object { $_.name -eq "iCloud-PassFlow.zip" } | Select-Object -First 1
        
        if ($asset) {
            Write-Host "Downloading $($asset.name)..."
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -ErrorAction Stop
            if ((Test-Path $zipPath) -and (Get-Item $zipPath).Length -gt 1000) {
                $downloaded = $true
            }
        }
    } catch {
        # Fallback to local dist if available
    }
}

# Method 3: Local build package fallback (for offline or local development testing)
if (!$downloaded) {
    $localDist = Join-Path $PSScriptRoot "dist\iCloud-PassFlow.zip"
    if (Test-Path $localDist) {
        Write-Host "Using local package from dist..."
        Copy-Item -Path $localDist -Destination $zipPath -Force
        $downloaded = $true
    }
}

if (!$downloaded) {
    Write-Host "ERROR: Could not download application package from GitHub release." -ForegroundColor Red
    exit 1
}

Write-Host "Extracting files..."
Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force
Remove-Item -Path $zipPath -Force

$exePath = Join-Path $InstallDir "iCloud-PassFlow.exe"
if (!(Test-Path $exePath)) {
    Write-Host "ERROR: Executable not found in package." -ForegroundColor Red
    exit 1
}

# 4. Register standard Startup Key (Requires NO Administrator privileges)
Write-Host "Registering silent background startup..."
Unregister-ScheduledTask -TaskName "iCloud-PassFlow" -Confirm:$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "iCloudPasswordsPlus" -Confirm:$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "iCloudPasswords+" -Confirm:$false -ErrorAction SilentlyContinue

# Clean up legacy startup entries from previous versions
$runKeys = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce"
)
$appNames = @("iCloud-PassFlow", "iCloudPasswords+", "iCloudPasswordsPlus", "iCloudPassLauncher", "iCloudPass")
foreach ($rk in $runKeys) {
    if (Test-Path $rk) {
        foreach ($name in $appNames) {
            Remove-ItemProperty -Path $rk -Name $name -ErrorAction SilentlyContinue
        }
    }
}

$saKeys = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
)
$saNames = @("iCloud-PassFlow", "iCloudPasswords+", "iCloudPasswordsPlus", "iCloudPassLauncher", "iCloudPass", "iCloudPass.vbs", "iCloudAutoTyper.vbs", "iCloud-PassFlow.lnk", "iCloudPasswords+.lnk", "iCloudPasswordsPlus.lnk")
foreach ($sak in $saKeys) {
    if (Test-Path $sak) {
        foreach ($name in $saNames) {
            Remove-ItemProperty -Path $sak -Name $name -ErrorAction SilentlyContinue
        }
    }
}

Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "iCloud-PassFlow" -Value "`"$exePath`""

# 5. Create Uninstaller and Register in Windows Settings ("Installed Apps") & Control Panel
Write-Host "Registering uninstaller in Windows Settings..."
$uninstallScript = @'
$ProgressPreference = 'SilentlyContinue'

# If executed directly from inside the installation directory, clone to TEMP to avoid self-locking
if ($PSScriptRoot -and ($PSScriptRoot -like "*\Programs\iCloud*")) {
    $tempScript = Join-Path $env:TEMP "uninstall_iCloud-PassFlow.ps1"
    Copy-Item -Path $PSCommandPath -Destination $tempScript -Force
    Start-Process powershell.exe -ArgumentList "-ExecutionPolicy Bypass -WindowStyle Hidden -File `"$tempScript`"" -WindowStyle Hidden
    exit
}

# 1. Stop any running instances
Stop-Process -Name "iCloud-PassFlow" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "iCloudPasswords+" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "iCloudPasswordsPlus" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "iCloudPassLauncher" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "iCloudPass" -Force -ErrorAction SilentlyContinue

Start-Sleep -Milliseconds 300

# 2. Remove Startup Registry Keys
$runKeys = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce"
)
$appNames = @("iCloud-PassFlow", "iCloudPasswords+", "iCloudPasswordsPlus", "iCloudPassLauncher", "iCloudPass")
foreach ($rk in $runKeys) {
    if (Test-Path $rk) {
        foreach ($name in $appNames) {
            Remove-ItemProperty -Path $rk -Name $name -ErrorAction SilentlyContinue
        }
    }
}

# 3. Remove StartupApproved Registry Entries (Task Manager startup list)
$saKeys = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
)
$saNames = @("iCloud-PassFlow", "iCloudPasswords+", "iCloudPasswordsPlus", "iCloudPassLauncher", "iCloudPass", "iCloudPass.vbs", "iCloudAutoTyper.vbs", "iCloud-PassFlow.lnk", "iCloudPasswords+.lnk", "iCloudPasswordsPlus.lnk")
foreach ($sak in $saKeys) {
    if (Test-Path $sak) {
        foreach ($name in $saNames) {
            Remove-ItemProperty -Path $sak -Name $name -ErrorAction SilentlyContinue
        }
    }
}

# 4. Remove any Scheduled Tasks
Unregister-ScheduledTask -TaskName "iCloud-PassFlow" -Confirm:$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "iCloudPasswords+" -Confirm:$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "iCloudPasswordsPlus" -Confirm:$false -ErrorAction SilentlyContinue

# 5. Remove Startup & Start Menu Shortcuts
$shortcutFolders = @(
    "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup",
    "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
)
foreach ($folder in $shortcutFolders) {
    if (Test-Path $folder) {
        Get-ChildItem -Path $folder -Recurse -ErrorAction SilentlyContinue | Where-Object {
            $_.Name -like "*iCloud*"
        } | Remove-Item -Force -ErrorAction SilentlyContinue
    }
}

# 6. Remove System Tray notification cache
$notifyPath = "HKCU:\Control Panel\NotifyIconSettings"
if (Test-Path $notifyPath) {
    Get-ChildItem -Path $notifyPath -ErrorAction SilentlyContinue | ForEach-Object {
        $item = Get-ItemProperty -Path $_.PsPath -ErrorAction SilentlyContinue
        if ($item.ExecutablePath -like "*iCloud*") {
            Remove-Item -Path $_.PsPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# 7. Remove Windows Settings & Control Panel Uninstall Registration
$uninstallKeys = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\iCloud-PassFlow",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\iCloudPasswords+",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\iCloudPasswordsPlus"
)
foreach ($uk in $uninstallKeys) {
    Remove-Item -Path $uk -Recurse -Force -ErrorAction SilentlyContinue
}

# 8. Remove Installation Directories cleanly
$dirsToRemove = @(
    "$env:LOCALAPPDATA\Programs\iCloud-PassFlow",
    "$env:LOCALAPPDATA\Programs\iCloudPasswords+",
    "$env:LOCALAPPDATA\Programs\iCloudPasswordsPlus"
)
foreach ($dir in $dirsToRemove) {
    if (Test-Path $dir) {
        for ($i = 0; $i -lt 5; $i++) {
            Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
            if (!(Test-Path $dir)) { break }
        }
    }
}

# 9. Clean up temporary script if running from TEMP
if ($PSCommandPath -and ($PSCommandPath -like "*\Temp\*")) {
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c timeout /t 1 /nobreak >nul & del /f /q `"$PSCommandPath`"" -WindowStyle Hidden
}
'@
Set-Content -Path "$InstallDir\uninstall.ps1" -Value $uninstallScript

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\iCloud-PassFlow"
if (!(Test-Path $uninstallKey)) { New-Item -Path $uninstallKey -Force | Out-Null }
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "iCloud-PassFlow"
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "Nitish Dhamu"
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value "$exePath,0"
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $uninstallKey -Name "InstallDate" -Value ((Get-Date).ToString("yyyyMMdd"))

$sizeKB = [math]::Round(((Get-ChildItem -Path $InstallDir -Recurse -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1KB))
if ($sizeKB -gt 0) {
    Set-ItemProperty -Path $uninstallKey -Name "EstimatedSize" -Value $sizeKB -Type DWord
}

$uninstRunner = "& { `$src = Join-Path `$env:LOCALAPPDATA 'Programs\iCloud-PassFlow\uninstall.ps1'; `$tmp = Join-Path `$env:TEMP 'uninstall_iCloud-PassFlow.ps1'; if (Test-Path `$src) { Copy-Item `$src `$tmp -Force; & `$tmp; Remove-Item `$tmp -Force -ErrorAction SilentlyContinue } else { Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\iCloud-PassFlow' -Recurse -Force -ErrorAction SilentlyContinue; Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'iCloud-PassFlow' -ErrorAction SilentlyContinue; Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' 'iCloud-PassFlow' -ErrorAction SilentlyContinue } }"
$uninstCmd = "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -Command `"$uninstRunner`""

Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value $uninstCmd
Set-ItemProperty -Path $uninstallKey -Name "QuietUninstallString" -Value $uninstCmd
Set-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -Type DWord

# 6. Start the App
Start-Process -FilePath $exePath -WindowStyle Hidden

Write-Host "======================================================" -ForegroundColor Green
Write-Host "   INSTALLATION COMPLETE!                             " -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Green
Write-Host "iCloud-PassFlow is now running silently in the background."
