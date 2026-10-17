<#
.SYNOPSIS
    Baut das Update-Paket von Veyra und veroeffentlicht es optional als GitHub-Release.

.DESCRIPTION
    1. Liest die Version aus src/Veyra/Veyra.csproj (<Version>).
    2. dotnet publish (Release, win-x64, framework-dependent) nach artifacts/publish/<Version>.
    3. Packt alles in artifacts/Veyra-<Version>-win-x64.zip (Update-Paket) und baut
       artifacts/Veyra-Setup-<Version>.exe (Installer, Inno Setup).
    4. Mit -Publish: Tag v<Version> setzen, pushen und das GitHub-Release mit Setup und ZIP anlegen.
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
$project = Join-Path $root 'src\Veyra\Veyra.csproj'
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { 'C:\Program Files\dotnet\dotnet.exe' }
$gh = if (Get-Command gh -ErrorAction SilentlyContinue) { 'gh' } else { 'C:\Program Files\GitHub CLI\gh.exe' }

$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Keine <Version> in $project gefunden." }
$tag = "v$version"
Write-Host "Veyra $version" -ForegroundColor Cyan

$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts "publish\$version"
$zip = Join-Path $artifacts "Veyra-$version-win-x64.zip"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
& $dotnet publish $project -c Release -r win-x64 --self-contained false -p:DebugType=none -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen ($LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $publishDir 'Veyra.exe'))) { throw 'Veyra.exe fehlt im Publish-Ordner.' }

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "Update-Paket: $zip" -ForegroundColor Green
Write-Host "SHA-256: $hash"

# Installer (Inno Setup) - fuer Erstinstallationen inkl. Pruefung der .NET Desktop Runtime.
$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) nicht gefunden: winget install JRSoftware.InnoSetup --scope user' }
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$publishDir" (Join-Path $root 'installer\Veyra.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup fehlgeschlagen ($LASTEXITCODE)." }
$setup = Join-Path $artifacts "Veyra-Setup-$version.exe"
Write-Host "Installer: $setup" -ForegroundColor Green

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

    git tag -a $tag -m "Veyra $version"
    # git schreibt Fortschrittsmeldungen nach stderr; unter Windows PowerShell 5 waere das sonst ein Abbruch.
    $ErrorActionPreference = 'Continue'
    git push origin $tag 2>&1 | Write-Host
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) { throw "git push des Tags fehlgeschlagen ($LASTEXITCODE)." }

    $notesArgs = if ($NotesFile) { @('--notes-file', $NotesFile) } elseif ($Notes) { @('--notes', $Notes) } else { @('--generate-notes') }
    & $gh release create $tag $setup $zip --title "Veyra $version" @notesArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create fehlgeschlagen ($LASTEXITCODE)." }
    Write-Host "Release $tag veroeffentlicht." -ForegroundColor Green
}
finally {
    Pop-Location
}
