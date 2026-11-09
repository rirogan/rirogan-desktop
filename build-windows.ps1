$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$desktopProject = Join-Path $root 'src\Rirogan.Desktop.csproj'
$launcherProject = Join-Path $root 'launcher\Rirogan.Launcher.csproj'
$dist = Join-Path $root 'dist'
$desktopDir = Join-Path $dist 'Rirogan-App'
$launcherDir = Join-Path $dist 'Rirogan-Launcher'
$project = [xml](Get-Content -Raw $desktopProject)
$version = [string]$project.Project.PropertyGroup.Version
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'SDK .NET 10 nao encontrado. Instale o SDK antes de compilar.'
}

# Evita que arquivos removidos numa nova versao fiquem na pasta de publicacao.
foreach ($folder in @($desktopDir, $launcherDir)) {
    if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
}

Write-Host "Compilando o Desktop $version para Windows x64..."
dotnet publish $desktopProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o $desktopDir
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o Desktop.' }

Write-Host 'Compilando o Rirogan Launcher...'
dotnet publish $launcherProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false "-p:Version=$version" -o $launcherDir
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o launcher.' }

Write-Host 'Compile installer\Rirogan.iss no Inno Setup e rode generate-update-manifest.ps1.'
