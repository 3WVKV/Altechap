<#
.SYNOPSIS
    Compile Altéchap, produit l'installeur, et publie éventuellement la release GitHub.

.DESCRIPTION
    Trois étapes enchaînées :
      1. dotnet publish en autonome (self-contained) win-x64 → dist\app
         « Autonome » = le runtime .NET 8 est embarqué. Sans ça, chaque ami
         devrait installer le Desktop Runtime avant de lancer l'app ; c'est la
         première cause d'échec d'une distribution WPF.
      2. Inno Setup compile dist\app en dist\Altechap-Setup-<version>.exe
      3. (option -Release) gh crée la release GitHub taguée v<version> et y
         attache l'installeur — c'est ce que le vérificateur de l'app interroge.

.PARAMETER Version
    Écrase la version du .csproj (et l'y réécrit). Ex. : -Version 2.1.0

.PARAMETER Release
    Publie la release GitHub après compilation. Nécessite la CLI « gh » authentifiée.

.PARAMETER Notes
    Notes de version affichées dans le dialogue de mise à jour de l'app.
    Par défaut, GitHub génère la liste des commits.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Version 2.1.0 -Release -Notes "Correction du tri par initiative."
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $Release,
    [string] $Notes,
    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$csproj  = Join-Path $root 'Altechap.csproj'
$dist    = Join-Path $root 'dist'
$appDir  = Join-Path $dist 'app'
$iss     = Join-Path $root 'Installer\Altechap.iss'

function Step($msg) { Write-Host "`n=== $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "  OK  $msg" -ForegroundColor Green }

# ── 1. Version ───────────────────────────────────────────────────────────────
Step 'Version'
# Édition textuelle plutôt que XmlDocument : Get-Content lit en ANSI sous
# Windows PowerShell et Save() réencode tout le fichier — les accents des
# commentaires du .csproj y passaient. Ici on ne touche qu'à la balise Version,
# octet pour octet, encodage d'origine préservé.
$utf8   = [System.Text.UTF8Encoding]::new($false)
$csPath = (Resolve-Path $csproj).Path
$text   = [System.IO.File]::ReadAllText($csPath, $utf8)

if ($text -notmatch '<Version>([^<]+)</Version>') { throw "Balise <Version> introuvable dans $csproj" }

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version « $Version » invalide : attendu X.Y.Z" }
    # Instance plutôt que [regex]::Replace : seule la surcharge d'instance
    # accepte un nombre maximum de remplacements.
    $rx   = [regex]::new('<Version>[^<]+</Version>')
    $text = $rx.Replace($text, "<Version>$Version</Version>", 1)
    [System.IO.File]::WriteAllText($csPath, $text, $utf8)
    Ok "Altechap.csproj mis à jour → $Version"
} else {
    $Version = $Matches[1]
}
Ok "Version cible : $Version"

# ── 2. Publication .NET ──────────────────────────────────────────────────────
Step 'Compilation (self-contained win-x64)'
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }

dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:Version=$Version `
    -o $appDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish a échoué.' }

$exe = Join-Path $appDir 'Altechap.exe'
if (-not (Test-Path $exe)) { throw "Altechap.exe introuvable dans $appDir" }
$sizeMb = [math]::Round(((Get-ChildItem $appDir -Recurse -Force | Measure-Object Length -Sum).Sum / 1MB), 1)
Ok "Publié dans dist\app ($sizeMb Mo)"

# ── 2 bis. Contrôle d'intégrité de la publication ────────────────────────────
# Deux pièges silencieux, découverts en v2.0.1 : un installeur amputé de deux
# DLL s'était retrouvé publié, et l'application plantait au démarrage chez
# l'utilisateur sans qu'aucune étape n'ait signalé quoi que ce soit.
Step 'Vérification de la publication'

# 1) Les fichiers du cache NuGet portent parfois l'attribut « caché ». MSBuild
#    le préserve en copiant, et le scan par jokers d'Inno Setup ignore les
#    fichiers cachés : les dépendances disparaissaient de l'installeur.
$hidden = Get-ChildItem $appDir -Recurse -File -Force |
          Where-Object { $_.Attributes -band [IO.FileAttributes]::Hidden }
foreach ($f in $hidden) {
    $f.Attributes = $f.Attributes -band (-bnot [IO.FileAttributes]::Hidden)
}
if ($hidden.Count -gt 0) { Ok "$($hidden.Count) fichier(s) caché(s) normalisé(s)" }

# 2) Toute assembly déclarée dans deps.json doit exister sur le disque, sinon
#    le démarrage échoue sur un FileNotFoundException.
$deps    = Get-Content (Join-Path $appDir 'Altechap.deps.json') -Raw | ConvertFrom-Json
$missing = @()
foreach ($target in $deps.targets.PSObject.Properties) {
    foreach ($lib in $target.Value.PSObject.Properties) {
        if (-not $lib.Value.runtime) { continue }
        foreach ($asm in $lib.Value.runtime.PSObject.Properties.Name) {
            $leaf = Split-Path $asm -Leaf
            if (-not (Test-Path (Join-Path $appDir $leaf))) { $missing += $leaf }
        }
    }
}
$missing = @($missing | Sort-Object -Unique)
if ($missing.Count -gt 0) {
    throw "Dépendances déclarées dans deps.json mais absentes de dist\app :`n  " +
          ($missing -join "`n  ")
}
Ok "$((Get-ChildItem $appDir -Recurse -File).Count) fichiers, dépendances complètes"

if ($SkipInstaller) { Write-Host "`nTerminé (installeur ignoré)." -ForegroundColor Yellow; return }

# ── 3. Installeur Inno Setup ─────────────────────────────────────────────────
Step 'Installeur Inno Setup'
$iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $iscc) {
    foreach ($p in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { $iscc = $p; break }
    }
}
if (-not $iscc) {
    throw @'
Inno Setup 6 introuvable.
Installez-le puis relancez :   winget install -e --id JRSoftware.InnoSetup
'@
}

$isccLog = & $iscc "/DAppVersion=$Version" "/DSourceDir=$appDir" "/DOutputDir=$dist" $iss 2>&1
if ($LASTEXITCODE -ne 0) { $isccLog | Select-Object -Last 20; throw 'ISCC a échoué.' }

$setup = Join-Path $dist "Altechap-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "Installeur introuvable : $setup" }

# Inno Setup n'avertit pas quand son scan par jokers laisse des fichiers de
# côté : il faut compter ce qu'il a réellement empaqueté et le confronter à la
# source. C'est ce contrôle qui aurait évité la v2.0.1 défaillante.
$packed   = @($isccLog | Where-Object { $_ -match '^\s*Compressing:' }).Count
$expected = (Get-ChildItem $appDir -Recurse -File -Force).Count
if ($packed -ne $expected) {
    throw "L'installeur ne contient que $packed fichiers sur les $expected de dist\app. " +
          "Fichiers ignorés par Inno Setup (attribut caché ?) — installeur non publiable."
}
Ok "Installeur : $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) Mo, $packed fichiers)"

# ── 4. Release GitHub ────────────────────────────────────────────────────────
if (-not $Release) {
    Write-Host "`nTerminé. Pour publier la mise à jour :" -ForegroundColor Yellow
    Write-Host "  .\build.ps1 -Version $Version -Release -Notes ""...""" -ForegroundColor Yellow
    return
}

Step 'Publication de la release GitHub'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "La CLI GitHub est requise :   winget install -e --id GitHub.cli"
}

$tag = "v$Version"
# $args est une variable automatique de PowerShell : la réutiliser casse le splat.
$ghArgs = @('release', 'create', $tag, $setup, '--title', "Altéchap $tag")

# Notes passées par fichier : Windows PowerShell découpe un argument natif
# multi-ligne, et gh interprète alors les morceaux comme des noms d'assets
# (« no matches found for … »). Un fichier UTF-8 évite aussi que les accents
# arrivent mutilés sur la page de release.
$notesFile = $null
if ($Notes) {
    $notesFile = Join-Path ([System.IO.Path]::GetTempPath()) "altechap-notes-$Version.md"
    [System.IO.File]::WriteAllText($notesFile, $Notes, [System.Text.UTF8Encoding]::new($false))
    $ghArgs += @('--notes-file', $notesFile)
} else {
    $ghArgs += '--generate-notes'
}

try {
    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create a échoué (le tag $tag existe déjà ?)." }
}
finally {
    if ($notesFile -and (Test-Path $notesFile)) { Remove-Item $notesFile -Force }
}
Ok "Release $tag publiée — les clients la verront au prochain démarrage."
