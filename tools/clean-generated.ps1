param([string]$ReportPath = '')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (!(Test-Path -LiteralPath (Join-Path $projectRoot 'source\Mirage.csproj'))) { throw 'Esta não é a pasta esperada do Mirage.' }
$prefix = $projectRoot + [IO.Path]::DirectorySeparatorChar
$activePaths = @(Get-Process Mirage -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Path } catch { } } | Where-Object { $_ })
$removed = [Collections.Generic.List[object]]::new()
$skipped = [Collections.Generic.List[object]]::new()
$rootExecutable = Join-Path $projectRoot 'Mirage.exe'
$executableHash = if (Test-Path -LiteralPath $rootExecutable) { (Get-FileHash -LiteralPath $rootExecutable -Algorithm SHA256).Hash } else { $null }
function Remove-Generated([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return }
    $resolved = (Resolve-Path -LiteralPath $Path).ProviderPath
    if (!$resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw ('Caminho fora do projeto: ' + $resolved) }
    $item = Get-Item -LiteralPath $resolved -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $skipped.Add(@{path=$resolved;reason='redirecionamento'}); return }
    foreach ($active in $activePaths) {
        if ($active.Equals($resolved, [StringComparison]::OrdinalIgnoreCase) -or $active.StartsWith($resolved + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { $skipped.Add(@{path=$resolved;reason='aplicativo em uso'}); return }
    }
    if ($item.PSIsContainer) {
        $children = @(Get-ChildItem -LiteralPath $resolved -Force -Recurse -ErrorAction Stop)
        if ($children | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }) { $skipped.Add(@{path=$resolved;reason='contém redirecionamento'}); return }
        $bytes = ($children | Where-Object { !$_.PSIsContainer } | Measure-Object Length -Sum).Sum
        try { Remove-Item -LiteralPath $resolved -Recurse -Force }
        catch { if (Test-Path -LiteralPath $resolved) { $skipped.Add(@{path=$resolved;reason='arquivo bloqueado ou em uso'}); return }; throw }
    } else {
        $bytes = $item.Length
        try { Remove-Item -LiteralPath $resolved -Force }
        catch { if (Test-Path -LiteralPath $resolved) { $skipped.Add(@{path=$resolved;reason='arquivo bloqueado ou em uso'}); return }; throw }
    }
    $removed.Add(@{path=$resolved;bytes=[long]$bytes})
}
# Explicit generated directories only. Source code, releases in use, dist and user data are retained.
foreach ($relative in @('backups','work','qa-results','source\bin','source\obj','source\Tests\bin','source\Tests\obj','source\Frontend\node_modules','source\Frontend\dist','tools\sandbox','tools\__pycache__','tools\tests\__pycache__')) { Remove-Generated (Join-Path $projectRoot $relative) }
Get-ChildItem -LiteralPath $projectRoot -Directory | Where-Object { $_.Name -match '^release-\d+(\.\d+){1,2}$' } | ForEach-Object { Remove-Generated $_.FullName }
Get-ChildItem -LiteralPath $projectRoot -File | Where-Object { $_.Name -match '^preview([-.]|\.)' -or $_.Name -match '^Mirage-running-[\w.\-]+\.exe$' -or $_.Name -in @('Mirage.pdb','probe.txt') } | ForEach-Object { Remove-Generated $_.FullName }
$currentVersion = if (Test-Path -LiteralPath $rootExecutable) { [version](Get-Item -LiteralPath $rootExecutable).VersionInfo.ProductVersion.Split('+')[0] } else { [version]([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'source\Mirage.csproj') -Raw)).Project.PropertyGroup.Version }
$distribution = Join-Path $projectRoot 'dist'
if (Test-Path -LiteralPath $distribution) {
    Get-ChildItem -LiteralPath $distribution -File | ForEach-Object {
        if ($_.Name -match '^(?:Mirage-(?:Setup|Portable)-(?<version>\d+\.\d+\.\d+)-win-x64\.(?:exe|zip)|SHA256SUMS-(?<version>\d+\.\d+\.\d+)\.txt)$') {
            if ([version]$Matches.version -ne $currentVersion) { Remove-Generated $_.FullName }
        }
    }
}
$afterHash = if (Test-Path -LiteralPath $rootExecutable) { (Get-FileHash -LiteralPath $rootExecutable -Algorithm SHA256).Hash } else { $null }
if ($executableHash -and $afterHash -ne $executableHash) { throw 'O executável atual mudou durante a limpeza.' }
$report = [ordered]@{root=$projectRoot;removedBytes=($removed | Measure-Object -Property bytes -Sum).Sum;removed=$removed.ToArray();skipped=$skipped.ToArray();currentExecutableSha256=$afterHash}
if ($ReportPath) { $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding utf8NoBOM }
Write-Output ('Removidos: ' + [Math]::Round($report.removedBytes / 1GB, 2) + ' GiB')
if ($skipped.Count) { $skipped | ConvertTo-Json -Compress }
