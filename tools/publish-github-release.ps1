#!/usr/bin/env pwsh
# Publishes a GitHub Release for the current version to yalyoha/JTC.
# Requires $env:GH_TOKEN (Personal Access Token with `repo` or `public_repo` scope).
#
# Usage:
#   $env:GH_TOKEN = 'ghp_xxx'
#   .\tools\publish-github-release.ps1 -Version 0.3.17.2

param(
  [Parameter(Mandatory=$true)][string]$Version,
  [string]$Repo = 'yalyoha/JTC',
  [string]$DistDir = "$PSScriptRoot\..\dist"
)

$ErrorActionPreference = 'Stop'

if (-not $env:GH_TOKEN) {
  # Fallback: read from repo-local ghp.txt (must stay in .gitignore).
  $tokenFile = Join-Path (Split-Path $PSScriptRoot -Parent) 'ghp.txt'
  if (Test-Path $tokenFile) {
    $env:GH_TOKEN = (Get-Content $tokenFile -Raw).Trim()
  }
}
if (-not $env:GH_TOKEN) {
  throw "Set `$env:GH_TOKEN` or drop the PAT into ghp.txt in the repo root."
}

$tag = "v$Version"
$setup = Join-Path $DistDir "JTC-$tag-setup.exe"
$zip   = Join-Path $DistDir "JTC-$tag-win-x64.zip"
$notes = Join-Path $DistDir "RELEASE_NOTES_$Version.md"

foreach ($p in @($setup, $zip, $notes)) {
  if (-not (Test-Path $p)) { throw "Missing artifact: $p" }
}

$headers = @{
  Authorization = "Bearer $env:GH_TOKEN"
  Accept        = 'application/vnd.github+json'
  'X-GitHub-Api-Version' = '2022-11-28'
  'User-Agent'  = 'jtc-release-script'
}

$body = @{
  tag_name = $tag
  name     = $tag
  body     = Get-Content $notes -Raw
  draft    = $false
  prerelease = $false
} | ConvertTo-Json -Depth 4

Write-Host "Creating release $tag on $Repo..."
$release = Invoke-RestMethod -Method Post `
  -Uri "https://api.github.com/repos/$Repo/releases" `
  -Headers $headers -Body $body -ContentType 'application/json'

Write-Host "Release id: $($release.id) — uploading assets..."

foreach ($file in @($setup, $zip)) {
  $name = Split-Path $file -Leaf
  Write-Host "  uploading $name..."
  $upload = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$name"
  Invoke-RestMethod -Method Post -Uri $upload `
    -Headers $headers -InFile $file -ContentType 'application/octet-stream' | Out-Null
}

Write-Host ""
Write-Host "Done: $($release.html_url)"
