param(
    [Parameter(Mandatory=$true)]
    [string]$XboxIP,
    [string]$Username = "DevToolsUser",
    [string]$Password
)

# Upload Content folder to Xbox's LocalState via Dev Portal REST API
$ContentPath = Join-Path $PSScriptRoot "..\Content"
if (-not (Test-Path $ContentPath)) {
    Write-Error "Content folder not found at $ContentPath"
    exit 1
}

# Prompt for password if not provided
if (-not $Password) {
    $secPass = Read-Host "Enter Xbox Device Portal password" -AsSecureString
    $cred = New-Object System.Management.Automation.PSCredential($Username, $secPass)
} else {
    $secPass = ConvertTo-SecureString $Password -AsPlainText -Force
    $cred = New-Object System.Management.Automation.PSCredential($Username, $secPass)
}

$BaseUrl = "https://${XboxIP}:11443/api/filesystem/apps/file"
$PackageName = "FridayNightFunkin.MonoGame_1.0.0.0_x64__g4pcqteb2n0dr"

# Build common params for Invoke-RestMethod (handle PS version differences for SSL)
$restParams = @{ Credential = $cred }
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $restParams['SkipCertificateCheck'] = $true
} else {
    # PowerShell 5.x: ignore self-signed cert
    Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class TrustAll : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }
}
"@
    [System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAll
}

Write-Host "Uploading Content to Xbox at $XboxIP ..." -ForegroundColor Green
Write-Host "Package: $PackageName" -ForegroundColor Yellow
Write-Host ""

$files = Get-ChildItem $ContentPath -Recurse -File
$total = $files.Count
$i = 0
$failed = 0

foreach ($file in $files) {
    $i++
    $relativePath = $file.FullName.Substring($ContentPath.Length + 1).Replace("\", "/")
    $xboxPath = "LocalState/Content/$relativePath"

    $pct = [Math]::Round($i / $total * 100)
    Write-Progress -Activity "Uploading to Xbox" -Status "$pct% ($i/$total) $relativePath" -PercentComplete $pct

    $url = "${BaseUrl}?knownfolderid=LocalAppData&packagefullname=${PackageName}&path=${xboxPath}"

    try {
        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        Invoke-RestMethod -Uri $url -Method Put -Body $bytes -ContentType "application/octet-stream" @restParams -ErrorAction Stop | Out-Null
    }
    catch {
        $failed++
        Write-Warning "Failed: $relativePath - $_"
    }
}

Write-Host ""
if ($failed -eq 0) {
    Write-Host "Done! Uploaded $total files to Xbox LocalState/Content/" -ForegroundColor Green
} else {
    Write-Host "Done! Uploaded $($total - $failed)/$total files ($failed failed)" -ForegroundColor Yellow
}
Write-Host "You can now launch the game on Xbox." -ForegroundColor Green
