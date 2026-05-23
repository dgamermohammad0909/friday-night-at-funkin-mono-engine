param(
    [string]$ContentPath = (Join-Path $PSScriptRoot "..\Content"),
    [string]$OutputZip = (Join-Path $PSScriptRoot "content.zip")
)

<#
.SYNOPSIS
    Package the Content folder into a zip for uploading to GitHub Releases.
    The Xbox game downloads this zip on first launch.

.DESCRIPTION
    Creates content.zip containing all game assets (images, audio, data, fonts).
    Upload this to a GitHub Release, then update CONTENT_URL in ContentDownloader.cs.

.EXAMPLE
    # Create content.zip
    .\package_content.ps1

    # Then upload to GitHub:
    # 1. Go to your repo → Releases → Create new release
    # 2. Tag: content-v1
    # 3. Attach content.zip as a release asset
    # 4. Update ContentDownloader.cs CONTENT_URL with the download link
    #
    # Or use GitHub CLI:
    # gh release create content-v1 content.zip --title "Game Content v1"
#>

if (-not (Test-Path $ContentPath)) {
    Write-Error "Content folder not found at: $ContentPath"
    exit 1
}

# Remove old zip if exists
if (Test-Path $OutputZip) {
    Remove-Item $OutputZip -Force
}

$files = Get-ChildItem $ContentPath -Recurse -File
$totalSize = ($files | Measure-Object -Property Length -Sum).Sum
$totalMB = [math]::Round($totalSize / 1MB, 1)

Write-Host ""
Write-Host "Packaging Content for GitHub Release" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Green
Write-Host "Source:  $ContentPath" -ForegroundColor Yellow
Write-Host "Output:  $OutputZip" -ForegroundColor Yellow
Write-Host "Files:   $($files.Count)" -ForegroundColor Yellow
Write-Host "Size:    $totalMB MB (uncompressed)" -ForegroundColor Yellow
Write-Host ""

Write-Host "Creating zip..." -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $ContentPath,
    $OutputZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true  # includeBaseDirectory: creates Content/ prefix inside zip
)

$zipSize = (Get-Item $OutputZip).Length
$zipMB = [math]::Round($zipSize / 1MB, 1)
$ratio = [math]::Round($zipSize / $totalSize * 100, 1)

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "Zip size: $zipMB MB ($ratio% of original)" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Upload to GitHub Releases:" -ForegroundColor White
Write-Host "     gh release create content-v1 `"$OutputZip`" --title `"Game Content v1`"" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  2. Get the download URL (right-click asset → Copy link):" -ForegroundColor White
Write-Host "     https://github.com/USER/REPO/releases/download/content-v1/content.zip" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  3. Update ContentDownloader.cs:" -ForegroundColor White
Write-Host "     CONTENT_URL = `"https://github.com/USER/REPO/releases/download/content-v1/content.zip`"" -ForegroundColor DarkGray
Write-Host ""
