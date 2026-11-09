$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = [xml](Get-Content -Raw (Join-Path $root 'src\Rirogan.Desktop.csproj'))
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^(0|[1-9]\d{0,4})(\.(0|[1-9]\d{0,4})){2}$' -or
    ($version.Split('.') | Where-Object { [int]$_ -gt 65535 })) {
    throw 'Use uma versao com tres numeros, por exemplo 1.6.6.'
}

$inno = Get-Content -Raw (Join-Path $root 'installer\Rirogan.iss')
if ($inno -notmatch '(?m)^#define MyAppVersion "([^"]+)"' -or $Matches[1] -ne $version) {
    throw 'Iguale MyAppVersion em installer\Rirogan.iss a Version do projeto.'
}

$appDir = Join-Path $root 'dist\Rirogan-App'
$app = Get-Item -LiteralPath (Join-Path $appDir 'Rirogan.Desktop.exe')
$launcher = Get-Item -LiteralPath (Join-Path $root 'dist\Rirogan-Launcher\Rirogan.exe')
$setupName = "Rirogan-Setup-$version-Windows-x64.exe"
$setup = Get-Item -LiteralPath (Join-Path $root "dist\installer\$setupName")
foreach ($file in @($app, $launcher, $setup)) {
    $info = $file.VersionInfo
    if ("$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)" -ne $version) {
        throw "Versao divergente em $($file.Name). Recompile o projeto e o instalador."
    }
}
if ($setup.LastWriteTimeUtc -lt $app.LastWriteTimeUtc -or $setup.LastWriteTimeUtc -lt $launcher.LastWriteTimeUtc) {
    throw 'Recompile o instalador no Inno Setup depois de compilar o projeto.'
}
if ($setup.Length -gt 300MB) { throw 'O instalador excede o limite do atualizador anterior.' }

$site = Join-Path $root 'dist\site'
$release = Join-Path $site "releases\$version"
$files = Join-Path $release 'files'
if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release -Recurse -Force }
New-Item -ItemType Directory -Path $files -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $appDir) {
    Copy-Item -LiteralPath $item.FullName -Destination $files -Recurse
}

$fileList = @(Get-ChildItem -LiteralPath $files -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($files.Length + 1).Replace('\', '/')
    if ($relative.Length -gt 200 -or $_.Length -gt 300MB -or
        ($relative.Split('/') | Where-Object { $_ -notmatch '^[a-zA-Z0-9_-][a-zA-Z0-9._-]*$' -or $_.EndsWith('.') -or $_ -match '^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)' })) {
        throw "Arquivo fora do formato esperado: $relative"
    }
    [ordered]@{
        path = $relative
        size = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
if ($fileList.Count -lt 1 -or $fileList.Count -gt 2048 -or ($fileList | ForEach-Object { $_.size } | Measure-Object -Sum).Sum -gt 800MB) {
    throw 'A lista de arquivos excede os limites do launcher.'
}
foreach ($required in @('Rirogan.Desktop.exe', 'Rirogan.Desktop.dll', 'Rirogan.Desktop.runtimeconfig.json', 'Rirogan.Desktop.deps.json')) {
    if ($required -notin $fileList.path) { throw "Arquivo de publicacao ausente: $required" }
}

$utf8 = New-Object System.Text.UTF8Encoding $false
$manifest = [ordered]@{ version = $version; files = $fileList } | ConvertTo-Json -Depth 4 -Compress
if ($utf8.GetByteCount($manifest) -gt 512KB) { throw 'O manifesto excede 512 KB.' }
[System.IO.File]::WriteAllText((Join-Path $release 'files.json'), $manifest, $utf8)
Copy-Item -LiteralPath $setup.FullName -Destination (Join-Path $site $setupName) -Force
$stable = @{ version = $version } | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText((Join-Path $site 'stable.json'), $stable, $utf8)

Write-Host "1. Publique dist\site\releases\$version na pasta public_html/software/releases/."
Write-Host "2. Publique dist\site\$setupName na pasta public_html/software/."
Write-Host '3. Depois de conferir os downloads, publique dist\site\stable.json por ultimo.'
