param(
    [Parameter(Mandatory=$true)]
    [string]$XboxIP,
    [string]$Username = "DevToolsUser",
    [string]$Password,
    [string]$Path = "LocalState",
    [string]$Download,
    [string]$OutDir = "."
)

<#
.SYNOPSIS
    Browse and download files from your Xbox UWP app's storage via Device Portal REST API.
    No website needed — works entirely from PowerShell.

.EXAMPLE
    # List root of app's LocalState
    .\browse_xbox_files.ps1 -XboxIP 192.168.1.50

    # List Content folder
    .\browse_xbox_files.ps1 -XboxIP 192.168.1.50 -Path "LocalState\Content"

    # List a subfolder
    .\browse_xbox_files.ps1 -XboxIP 192.168.1.50 -Path "LocalState\Content\data\levels"

    # Download a specific file
    .\browse_xbox_files.ps1 -XboxIP 192.168.1.50 -Download "LocalState\Content\data\levels\week1.json"

    # Download a file to a specific folder
    .\browse_xbox_files.ps1 -XboxIP 192.168.1.50 -Download "LocalState\Content\data\levels\week1.json" -OutDir "C:\temp"
#>

$PackageName = "FridayNightFunkin.MonoGame_1.0.0.0_x64__g4pcqteb2n0dr"
$BaseUrl = "https://${XboxIP}:11443"

# Prompt for password if not provided
if (-not $Password) {
    $secPass = Read-Host "Enter Xbox Device Portal password" -AsSecureString
    $cred = New-Object System.Management.Automation.PSCredential($Username, $secPass)
} else {
    $secPass = ConvertTo-SecureString $Password -AsPlainText -Force
    $cred = New-Object System.Management.Automation.PSCredential($Username, $secPass)
}

# Handle SSL for self-signed certs
$restParams = @{ Credential = $cred }
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $restParams['SkipCertificateCheck'] = $true
} else {
    try {
        Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class TrustAll : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }
}
"@
        [System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAll
    } catch { }
}

function List-Files($folderPath) {
    $encodedPath = [System.Uri]::EscapeDataString($folderPath)
    $url = "${BaseUrl}/api/filesystem/apps/files?knownfolderid=LocalAppData&packagefullname=${PackageName}&path=${encodedPath}"
    
    try {
        $result = Invoke-RestMethod -Uri $url -Method Get @restParams -ErrorAction Stop
        
        Write-Host ""
        Write-Host "  Directory: $folderPath" -ForegroundColor Cyan
        Write-Host "  ========================================" -ForegroundColor DarkGray
        
        $items = @()
        if ($result.Items) { $items = $result.Items }
        elseif ($result) { $items = @($result) }
        
        if ($items.Count -eq 0) {
            Write-Host "  (empty)" -ForegroundColor DarkGray
            return
        }
        
        foreach ($item in $items) {
            $name = $item.Name
            $size = $item.Size
            $type = $item.Type
            
            if ($type -eq 32) {
                # Directory
                Write-Host "  [DIR]  $name" -ForegroundColor Yellow
            } else {
                # File
                $sizeStr = if ($size -gt 1MB) { "{0:N1} MB" -f ($size / 1MB) }
                           elseif ($size -gt 1KB) { "{0:N1} KB" -f ($size / 1KB) }
                           else { "$size B" }
                Write-Host ("  {0,-40} {1,10}" -f $name, $sizeStr) -ForegroundColor White
            }
        }
        Write-Host ""
    }
    catch {
        Write-Error "Failed to list '$folderPath': $_"
    }
}

function Download-File($filePath) {
    $encodedPath = [System.Uri]::EscapeDataString($filePath)
    $url = "${BaseUrl}/api/filesystem/apps/file?knownfolderid=LocalAppData&packagefullname=${PackageName}&path=${encodedPath}"
    
    $fileName = Split-Path $filePath -Leaf
    $outPath = Join-Path $OutDir $fileName
    
    Write-Host "Downloading: $filePath" -ForegroundColor Green
    Write-Host "Saving to:   $outPath" -ForegroundColor Yellow
    
    try {
        Invoke-RestMethod -Uri $url -Method Get -OutFile $outPath @restParams -ErrorAction Stop
        $size = (Get-Item $outPath).Length
        Write-Host "Done! ($size bytes)" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to download '$filePath': $_"
    }
}

# Main
Write-Host ""
Write-Host "Xbox File Browser - $PackageName" -ForegroundColor Green
Write-Host "Device: $XboxIP" -ForegroundColor DarkGray

if ($Download) {
    Download-File $Download
} else {
    List-Files $Path
}
