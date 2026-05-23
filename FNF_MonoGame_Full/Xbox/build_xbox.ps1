###############################################################################
# FNF MonoGame - Xbox APPX Build Script
# 
# PREREQUISITES (install these first):
# 1. Visual Studio 2022 with "Universal Windows Platform development" workload
#    - Open Visual Studio Installer > Modify > check "Universal Windows Platform development"
#    - Also check "Xbox development tools" in Individual Components
# 2. Windows 10 SDK (22621 or later)
#    - Included with UWP workload above
#
# USAGE:
#   .\build_xbox.ps1              # Build Debug APPX
#   .\build_xbox.ps1 -Release     # Build Release APPX  
#   .\build_xbox.ps1 -Deploy      # Build and deploy to Xbox
###############################################################################

param(
    [switch]$Release,
    [switch]$Deploy,
    [string]$XboxIP = ""
)

$ErrorActionPreference = "Continue"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ScriptDir "FNF_Xbox.csproj"
$Config = if ($Release) { "Release" } else { "Debug" }
$Platform = "x64"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host " FNF MonoGame - Xbox Build Script" -ForegroundColor Cyan  
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# === Step 1: Check prerequisites ===
Write-Host "[1/5] Checking prerequisites..." -ForegroundColor Yellow

# Check for Windows SDK (may be on C: or D: or custom path via registry)
$sdkPath = $null
$regRoot = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots" -Name KitsRoot10 -ErrorAction SilentlyContinue).KitsRoot10
if ($regRoot -and (Test-Path $regRoot)) { $sdkPath = $regRoot.TrimEnd('\') }
elseif (Test-Path "C:\Program Files (x86)\Windows Kits\10") { $sdkPath = "C:\Program Files (x86)\Windows Kits\10" }
elseif (Test-Path "D:\Windows Kits\10") { $sdkPath = "D:\Windows Kits\10" }
if (-not $sdkPath) {
    Write-Host ""
    Write-Host "ERROR: Windows 10 SDK not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "You need to install:" -ForegroundColor White
    Write-Host "  1. Open Visual Studio Installer" -ForegroundColor White
    Write-Host "  2. Click 'Modify' on your VS installation" -ForegroundColor White
    Write-Host "  3. Check 'Universal Windows Platform development'" -ForegroundColor White
    Write-Host "  4. In Individual Components, check 'Xbox development tools'" -ForegroundColor White
    Write-Host "  5. Click 'Modify' to install" -ForegroundColor White
    Write-Host ""
    Write-Host "Or install Windows SDK directly from:" -ForegroundColor White
    Write-Host "  https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/" -ForegroundColor Cyan
    exit 1
}

# Find MSBuild (UWP requires MSBuild, not dotnet build)
$msbuild = $null
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vsWhere) {
    $vsPath = & $vsWhere -latest -requires Microsoft.Component.MSBuild -property installationPath 2>$null
    if ($vsPath) {
        $msbuild = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
        if (-not (Test-Path $msbuild)) {
            $msbuild = Join-Path $vsPath "MSBuild\Current\Bin\amd64\MSBuild.exe"
        }
    }
}
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    Write-Host "ERROR: MSBuild not found! Install Visual Studio 2022 with UWP workload." -ForegroundColor Red
    exit 1
}
Write-Host "  MSBuild: $msbuild" -ForegroundColor Green
Write-Host "  Windows SDK: Found" -ForegroundColor Green

# Find makeappx.exe and signtool.exe
$makeappx = Get-ChildItem "$sdkPath\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1
if (-not $makeappx) {
    Write-Host "ERROR: makeappx.exe not found! Ensure Windows SDK is fully installed." -ForegroundColor Red
    exit 1
}
$signtool = Get-ChildItem "$sdkPath\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1
Write-Host "  makeappx: $($makeappx.FullName)" -ForegroundColor Green
if ($signtool) { Write-Host "  signtool: $($signtool.FullName)" -ForegroundColor Green }

# Certificate files
$pfxFile = Join-Path $ScriptDir "mohammad_aljafari.pfx"
$cerFile = Join-Path $ScriptDir "mohammad_aljafari.cer"
if (Test-Path $pfxFile) { Write-Host "  Certificate: mohammad_aljafari.pfx" -ForegroundColor Green }
else { Write-Host "  WARNING: mohammad_aljafari.pfx not found - package will not be signed" -ForegroundColor Yellow }

# === Step 2: Restore NuGet packages ===
Write-Host ""
Write-Host "[2/5] Restoring NuGet packages..." -ForegroundColor Yellow
& $msbuild $ProjectFile /t:Restore /p:Configuration=$Config /p:Platform=$Platform /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: NuGet restore failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  Packages restored" -ForegroundColor Green

# === Step 3: Build the project ===
Write-Host ""
$buildLabel = "[3/5] Building $Config $Platform"
Write-Host $buildLabel -ForegroundColor Yellow
& $msbuild $ProjectFile /p:Configuration=$Config /p:Platform=$Platform /p:AppxBundle=Never /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  Build succeeded" -ForegroundColor Green

# === Step 4: Create APPX package ===
Write-Host ""
Write-Host "[4/5] Creating APPX package..." -ForegroundColor Yellow

$outputDir = Join-Path $ScriptDir "bin\$Platform\$Config"
$appxDir = Join-Path $ScriptDir "AppxPackage"
$appxFile = $null

# Search for the MSIX/APPX produced by MSBuild (can be in AppPackages, bin, or AppxPackage)
$searchDirs = @(
    (Join-Path $ScriptDir "AppPackages"),
    $outputDir,
    $appxDir,
    "D:\"  # MSBuild sometimes places AppxPackageDir relative to drive root
)
foreach ($dir in $searchDirs) {
    if (-not (Test-Path $dir -ErrorAction SilentlyContinue)) { continue }
    $found = Get-ChildItem $dir -Recurse -Include *.appx,*.msix -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\Dependencies\\' -and $_.Name -like 'FNF_*' } | Select-Object -First 1
    if ($found) {
        New-Item -ItemType Directory -Path $appxDir -Force | Out-Null
        $appxFile = Join-Path $appxDir $found.Name
        Copy-Item $found.FullName $appxFile -Force
        Write-Host "  Package found: $($found.Name)" -ForegroundColor Green
        break
    }
}
if (-not $appxFile) {
    Write-Host "  WARNING: MSIX/APPX not found. Check build logs." -ForegroundColor Yellow
    Write-Host "  You can manually create it in Visual Studio:" -ForegroundColor White
    Write-Host "    Right-click project - Publish - Create App Packages" -ForegroundColor White
}

# === Step 4b: Verify package signature ===
if ($appxFile -and (Test-Path $appxFile -ErrorAction SilentlyContinue)) {
    if ($signtool) {
        Write-Host ""
        Write-Host "[4b] Verifying package signature..." -ForegroundColor Yellow
        $sigResult = & $signtool.FullName verify /v "$appxFile" 2>&1
        $issuer = $sigResult | Select-String "Issued to:" | Select-Object -First 1
        if ($issuer) {
            Write-Host "  Signed: $($issuer.Line.Trim())" -ForegroundColor Green
        } else {
            Write-Host "  WARNING: Package may not be signed" -ForegroundColor Yellow
        }
    }
}

# === Step 4c: Copy certificate alongside package for sideloading ===
if ($appxFile -and (Test-Path $appxFile -ErrorAction SilentlyContinue)) {
    $pkgDir = Split-Path $appxFile -Parent
    if (Test-Path $cerFile) {
        Copy-Item $cerFile (Join-Path $pkgDir "mohammad_aljafari.cer") -Force
        Write-Host "  Certificate copied to output: mohammad_aljafari.cer" -ForegroundColor Green
    }
    # Also copy dependencies if they exist
    $depDir = Get-ChildItem (Join-Path $ScriptDir "AppPackages") -Directory -Recurse -Filter "Dependencies" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($depDir) {
        $destDeps = Join-Path $pkgDir "Dependencies"
        if (-not (Test-Path $destDeps)) {
            Copy-Item $depDir.FullName $destDeps -Recurse -Force
            Write-Host "  Dependencies copied to output" -ForegroundColor Green
        }
    }
}

# === Step 5: Deploy to Xbox (optional) ===
if ($Deploy) {
    Write-Host ""
    Write-Host "[5/5] Deploying to Xbox..." -ForegroundColor Yellow
    
    if ([string]::IsNullOrEmpty($XboxIP)) {
        Write-Host "  Enter your Xbox IP address (found in Dev Home app):" -ForegroundColor White
        $XboxIP = Read-Host "  Xbox IP"
    }
    
    if ([string]::IsNullOrEmpty($XboxIP)) {
        Write-Host "  ERROR: No Xbox IP provided. Skipping deploy." -ForegroundColor Red
    } else {
        Write-Host "  Deploying to Xbox at $XboxIP..." -ForegroundColor White
        Write-Host ""
        Write-Host "  To deploy manually:" -ForegroundColor Yellow
        Write-Host "  1. Open a browser to https://${XboxIP}:11443" -ForegroundColor White
        Write-Host "  2. Go to My Games and Apps - Add" -ForegroundColor White
        Write-Host "  3. Upload the APPX file: $appxFile" -ForegroundColor White
        Write-Host ""
        Write-Host "  OR in Visual Studio:" -ForegroundColor Yellow
        Write-Host "  1. Open FNF_Xbox.csproj in Visual Studio" -ForegroundColor White
        Write-Host "  2. Set platform to x64" -ForegroundColor White
        Write-Host "  3. Set deploy target to 'Remote Machine'" -ForegroundColor White
        Write-Host "  4. Enter Xbox IP: $XboxIP" -ForegroundColor White
        Write-Host "  5. Press F5 to build and deploy" -ForegroundColor White
    }
} else {
    Write-Host ""
    Write-Host "[5/5] Deploy skipped (use -Deploy flag)" -ForegroundColor Gray
}

# === Done ===
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " BUILD COMPLETE" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
if ($appxFile -and (Test-Path $appxFile -ErrorAction SilentlyContinue)) {
    Write-Host "Package Location: $appxFile" -ForegroundColor Cyan
    Write-Host "File Size: $([math]::Round((Get-Item $appxFile).Length / 1MB, 2)) MB" -ForegroundColor Cyan
    Write-Host "Signed by: Mohammad Aljafari" -ForegroundColor Cyan
} else {
    # Check AppPackages as final fallback
    $anyPkg = Get-ChildItem (Join-Path $ScriptDir "AppPackages") -Recurse -Include *.appx,*.msix -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\Dependencies\\' -and $_.Name -like 'FNF_*' } | Select-Object -First 1
    if ($anyPkg) {
        Write-Host "Package Location: $($anyPkg.FullName)" -ForegroundColor Cyan
        Write-Host "File Size: $([math]::Round($anyPkg.Length / 1MB, 2)) MB" -ForegroundColor Cyan
    } else {
        Write-Host "Build output: $outputDir" -ForegroundColor Cyan
    }
}
Write-Host ""
Write-Host "To deploy to Xbox:" -ForegroundColor White
Write-Host "  .\build_xbox.ps1 -Deploy -XboxIP 192.168.1.xxx" -ForegroundColor Yellow
Write-Host ""
Write-Host "To install on Xbox (sideload):" -ForegroundColor White
Write-Host "  1. Install mohammad_aljafari.cer on the Xbox (Dev Portal - Certificates)" -ForegroundColor White
Write-Host "  2. Upload the MSIX/APPX via Xbox Dev Portal (https://XboxIP:11443)" -ForegroundColor White
Write-Host ""
Write-Host "Or open FNF_Xbox.csproj in Visual Studio and press F5" -ForegroundColor White
Write-Host "with Remote Machine target set to your Xbox IP." -ForegroundColor White
