$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Host "Installing iCloud PassFlow..." -ForegroundColor Cyan

$repo = "nitishdhamu/iCloud-PassFlow"
$setupName = "iCloudPassFlow-Setup.exe"
$tempSetup = Join-Path $env:TEMP $setupName

Remove-Item -Path $tempSetup -Force -ErrorAction SilentlyContinue
$downloaded = $false

# Method 1: Local package if running from local repository
if ($PSScriptRoot) {
    $localSetup = Join-Path $PSScriptRoot "dist\$setupName"
    if (Test-Path $localSetup) {
        Write-Host "Using package from local dist..."
        Copy-Item -Path $localSetup -Destination $tempSetup -Force
        $downloaded = $true
    }
}

# Method 2: Direct GitHub Release download
if (!$downloaded) {
    $directUrl = "https://github.com/$repo/releases/latest/download/$setupName"
    try {
        Write-Host "Downloading $setupName..."
        Invoke-WebRequest -Uri $directUrl -OutFile $tempSetup -UseBasicParsing -ErrorAction Stop
        if ((Test-Path $tempSetup) -and (Get-Item $tempSetup).Length -gt 100000) {
            $downloaded = $true
        }
    } catch {
        # Fall through to GitHub API
    }
}

# Method 3: GitHub API lookup
if (!$downloaded) {
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -UseBasicParsing -ErrorAction Stop
        $asset = $release.assets | Where-Object { $_.name -eq $setupName } | Select-Object -First 1
        
        if ($asset) {
            Write-Host "Downloading $($asset.name)..."
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tempSetup -UseBasicParsing -ErrorAction Stop
            if ((Test-Path $tempSetup) -and (Get-Item $tempSetup).Length -gt 100000) {
                $downloaded = $true
            }
        }
    } catch {
        # Fall through
    }
}

if (!$downloaded) {
    Write-Host "ERROR: Could not download $setupName from GitHub release." -ForegroundColor Red
    return
}

Write-Host "Running installer..."
$p = [System.Diagnostics.Process]::Start($tempSetup, "--silent")
$p.WaitForExit()
$exitCode = $p.ExitCode
$p.Dispose()

# Clean up temporary installer
Remove-Item -Path $tempSetup -Force -ErrorAction SilentlyContinue

if ($exitCode -ne 0) {
    Write-Host "ERROR: Installation failed with exit code $exitCode." -ForegroundColor Red
    return
}

$installedExe = "$env:LOCALAPPDATA\Programs\iCloud-PassFlow\iCloudPassFlow.exe"
if (!(Test-Path $installedExe)) {
    Write-Host "ERROR: Executable not found at $installedExe." -ForegroundColor Red
    return
}

# Ensure background process is running detached from terminal
if (!(Get-Process -Name "iCloudPassFlow" -ErrorAction SilentlyContinue)) {
    try {
        Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
            CommandLine = "`"$installedExe`""
            CurrentDirectory = "$env:LOCALAPPDATA\Programs\iCloud-PassFlow"
        } | Out-Null
    } catch {
        Start-Process -FilePath $installedExe -WorkingDirectory "$env:LOCALAPPDATA\Programs\iCloud-PassFlow" -WindowStyle Hidden
    }
}

Write-Host "======================================================" -ForegroundColor Green
Write-Host "   INSTALLATION COMPLETE!                             " -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Green
Write-Host "iCloud PassFlow is now running silently in the background."
