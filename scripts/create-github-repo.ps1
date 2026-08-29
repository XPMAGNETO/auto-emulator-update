param(
    [string]$Repository = "auto-emulator-update",
    [ValidateSet("public","private")]
    [string]$Visibility = "public"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required. Install it from https://cli.github.com/"
}

gh auth status
$Owner = (gh api user --jq .login).Trim()
if (-not $Owner) { throw "Could not determine the authenticated GitHub username." }

$BuildInfo = "src/AutoEmulatorUpdate.Core/BuildInfo.cs"
(Get-Content $BuildInfo -Raw).Replace(
    'OWNER/auto-emulator-update',
    "$Owner/$Repository"
) | Set-Content $BuildInfo -Encoding UTF8

git add $BuildInfo
git commit -m "chore: configure GitHub update repository" 2>$null

gh repo create $Repository "--$Visibility" --source=. --remote=origin --push
git push origin --tags

Write-Host ""
Write-Host "Repository created:"
Write-Host "https://github.com/$Owner/$Repository"
Write-Host ""
Write-Host "End users can use the GitHub Releases page. Release tags automatically build:"
Write-Host "  Windows Setup.exe"
Write-Host "  Linux AppImage / DEB"
Write-Host "  macOS DMG"
