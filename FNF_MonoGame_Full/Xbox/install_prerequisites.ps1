###############################################################################
# Install Xbox Development Prerequisites
# Run this as Administrator to install everything needed
###############################################################################

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host " Xbox Dev Prerequisites Installer" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as admin
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "WARNING: Run this script as Administrator for automatic installation." -ForegroundColor Yellow
    Write-Host ""
}

# === Option 1: VS Installer (recommended) ===
$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vs_installer.exe"
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"

if (Test-Path $vsWhere) {
    $vsPath = & $vsWhere -latest -property installationPath 2>$null
    if ($vsPath) {
        Write-Host "Found Visual Studio at: $vsPath" -ForegroundColor Green
        Write-Host ""
        Write-Host "Installing UWP + Xbox workloads via VS Installer..." -ForegroundColor Yellow
        Write-Host "(This will open the Visual Studio Installer)" -ForegroundColor Gray
        Write-Host ""
        
        # These are the component IDs needed for Xbox UWP development
        $components = @(
            "Microsoft.VisualStudio.Workload.Universal",          # UWP development
            "Microsoft.VisualStudio.ComponentGroup.UWP.Support",  # UWP build support
            "Microsoft.VisualStudio.Component.Windows10SDK.22621" # Windows SDK
        )
        
        $args = "modify --installPath `"$vsPath`" --passive"
        foreach ($c in $components) {
            $args += " --add $c"
        }
        
        Write-Host "Running: vs_installer.exe $args" -ForegroundColor Gray
        Start-Process $vsInstaller -ArgumentList $args.Split(" ") -Wait
        
        Write-Host ""
        Write-Host "Installation complete! Restart Visual Studio if it was open." -ForegroundColor Green
    }
} else {
    Write-Host "Visual Studio not found." -ForegroundColor Yellow
    Write-Host ""
}

# === Option 2: Standalone SDK download ===
Write-Host ""
Write-Host "If you prefer standalone installation:" -ForegroundColor White
Write-Host ""
Write-Host "1. Windows 10 SDK:" -ForegroundColor Cyan
Write-Host "   https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/" -ForegroundColor White
Write-Host ""
Write-Host "2. Xbox Dev Mode on your console:" -ForegroundColor Cyan  
Write-Host "   https://learn.microsoft.com/en-us/windows/uwp/xbox-apps/devkit-activation" -ForegroundColor White
Write-Host "   - Search 'Dev Mode Activation' in Xbox Store" -ForegroundColor White
Write-Host "   - Follow the activation steps with your dev account" -ForegroundColor White
Write-Host ""
Write-Host "3. After installing, run:" -ForegroundColor Cyan
Write-Host "   cd Xbox" -ForegroundColor Yellow
Write-Host "   .\build_xbox.ps1" -ForegroundColor Yellow
Write-Host ""
Write-Host "4. Or open Xbox\FNF_Xbox.csproj in Visual Studio" -ForegroundColor Cyan
Write-Host "   and press F5 with Remote Machine = your Xbox IP" -ForegroundColor White
