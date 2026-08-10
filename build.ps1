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
$xml = [xml](Get-Content $csproj -Raw)
$node = $xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version « $Version » invalide : attendu X.Y.Z" }
    $node.Version = $Version
    # Save() sur un chemin relatif écrirait ailleurs : on force l'absolu.
    $xml.Save((Resolve-Path $csproj).Path)
    Ok "Altechap.csproj mis à jour → $Version"
} else {
    $Version = $node.Version
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
$sizeMb = [math]::Round(((Get-ChildItem $appDir -Recurse | Measure-Object Length -Sum).Sum / 1MB), 1)
Ok "Publié dans dist\app ($sizeMb Mo)"

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

& $iscc "/DAppVersion=$Version" "/DSourceDir=$appDir" "/DOutputDir=$dist" $iss
if ($LASTEXITCODE -ne 0) { throw 'ISCC a échoué.' }

$setup = Join-Path $dist "Altechap-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "Installeur introuvable : $setup" }
Ok "Installeur : $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) Mo)"

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
if ($Notes) { $ghArgs += @('--notes', $Notes) } else { $ghArgs += '--generate-notes' }

& gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create a échoué (le tag $tag existe déjà ?)." }
Ok "Release $tag publiée — les clients la verront au prochain démarrage."
