<#
.SYNOPSIS
    Baut das Update-Paket von CustomTopBar und veroeffentlicht es optional als GitHub-Release.

.DESCRIPTION
    1. Liest die Version aus src/CustomTopBar/CustomTopBar.csproj (<Version>).
    2. dotnet publish (Release, win-x64, framework-dependent) nach artifacts/publish/<Version>.
    3. Packt alles in artifacts/CustomTopBar-<Version>-win-x64.zip.
    4. Mit -Publish: Tag v<Version> setzen, pushen und das GitHub-Release mit der ZIP anlegen.
       Installierte Leisten finden das Release dann automatisch ueber die Update-Pruefung.

.EXAMPLE
    .\build\release.ps1
    .\build\release.ps1 -Publish -NotesFile .\notes.md
#>
param(
    [switch]$Publish,
    [string]$NotesFile,
    [string]$Notes
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\CustomTopBar\CustomTopBar.csproj'
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { 'C:\Program Files\dotnet\dotnet.exe' }
$gh = if (Get-Command gh -ErrorAction SilentlyContinue) { 'gh' } else { 'C:\Program Files\GitHub CLI\gh.exe' }

$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Keine <Version> in $project gefunden." }
$tag = "v$version"
Write-Host "CustomTopBar $version" -ForegroundColor Cyan

$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts "publish\$version"
$zip = Join-Path $artifacts "CustomTopBar-$version-win-x64.zip"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
& $dotnet publish $project -c Release -r win-x64 --self-contained false -p:DebugType=none -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen ($LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $publishDir 'CustomTopBar.exe'))) { throw 'CustomTopBar.exe fehlt im Publish-Ordner.' }

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "Paket: $zip" -ForegroundColor Green
Write-Host "SHA-256: $hash"

if (-not $Publish) {
    Write-Host "Nicht veroeffentlicht. Zum Veroeffentlichen: .\build\release.ps1 -Publish" -ForegroundColor Yellow
    return
}

# Nur aus einem sauberen, gepushten Stand veroeffentlichen - das Release soll zum Code auf GitHub passen.
Push-Location $root
try {
    if (git status --porcelain) { throw 'Es gibt nicht committete Aenderungen. Erst committen und pushen.' }
    git fetch origin --quiet
    $local = git rev-parse HEAD
    $remote = git rev-parse '@{u}'
    if ($local -ne $remote) { throw 'Lokaler Stand ist nicht gepusht (git push).' }
    if (git tag --list $tag) { throw "Tag $tag existiert bereits. Version in der .csproj erhoehen." }

    git tag -a $tag -m "CustomTopBar $version"
    git push origin $tag

    $notesArgs = if ($NotesFile) { @('--notes-file', $NotesFile) } elseif ($Notes) { @('--notes', $Notes) } else { @('--generate-notes') }
    & $gh release create $tag $zip --title "CustomTopBar $version" @notesArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create fehlgeschlagen ($LASTEXITCODE)." }
    Write-Host "Release $tag veroeffentlicht." -ForegroundColor Green
}
finally {
    Pop-Location
}
