param(
    [string]$PublishDir = '',
    [string]$CompilerPath = '',
    [string]$OutputDir = '',
    [switch]$KeepPreviousPackagesForUpgradeTest
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$OutputDir) { $OutputDir = Join-Path $projectRoot 'dist' }
$buildApplication = !$PublishDir
if (!$PublishDir) { $PublishDir = Join-Path $projectRoot 'work\release' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$PublishDir = [IO.Path]::GetFullPath($PublishDir)
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
if ($buildApplication) {
    dotnet publish (Join-Path $projectRoot 'source\Mirage.csproj') -c Release -r win-x64 --self-contained true -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o aplicativo.' }
}
if (!(Test-Path -LiteralPath (Join-Path $PublishDir 'Mirage.exe'))) { throw 'Executável não encontrado na pasta de compilação indicada.' }
$version = (Get-Item -LiteralPath (Join-Path $PublishDir 'Mirage.exe')).VersionInfo.ProductVersion.Split('+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versão do aplicativo inválida.' }
if (!$CompilerPath) {
    $installed = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($installed) { $CompilerPath = $installed.Source }
    elseif (Test-Path -LiteralPath "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") { $CompilerPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
    else {
        $portableRoot = Join-Path $projectRoot 'work\toolchains\inno-portable'
        $CompilerPath = Join-Path $portableRoot 'package\bin\ISCC.exe'
        if (!(Test-Path -LiteralPath $CompilerPath)) {
            New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null
            npm pack innosetup@6.4.1 --ignore-scripts --prefer-offline --fetch-retries=1 --fetch-timeout=20000 --pack-destination $portableRoot --silent
            if ($LASTEXITCODE -ne 0) { throw 'Não foi possível obter o compilador portátil.' }
            tar -xf (Join-Path $portableRoot 'innosetup-6.4.1.tgz') -C $portableRoot
            if ($LASTEXITCODE -ne 0) { throw 'Não foi possível extrair o compilador portátil.' }
        }
    }
}
$signature = Get-AuthenticodeSignature -LiteralPath $CompilerPath
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Martijn Laan|Pyrsys|Jordan Russell') { throw 'Assinatura do compilador não confere.' }
$prerequisites = Join-Path $projectRoot 'work\prerequisites'
New-Item -ItemType Directory -Path $prerequisites -Force | Out-Null
$bootstrapper = Join-Path $prerequisites 'MicrosoftEdgeWebView2Setup.exe'
if (!(Test-Path -LiteralPath $bootstrapper)) {
    Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper -TimeoutSec 60
}
$signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Corporation,') { throw 'Assinatura do WebView2 não confere.' }
& $CompilerPath "/DAppVersion=$version" "/DPublishDir=$PublishDir" "/DPrerequisiteDir=$prerequisites" "/DDestinationDir=$OutputDir" (Join-Path $projectRoot 'installer\Mirage.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar o instalador.' }
$zip = Join-Path $OutputDir "Mirage-Portable-$version-win-x64.zip"
Copy-Item -LiteralPath (Join-Path $projectRoot 'source\Frontend\public\licenses') -Destination $PublishDir -Recurse -Force
Compress-Archive -LiteralPath (Join-Path $PublishDir 'Mirage.exe'),(Join-Path $projectRoot 'installer\DISTRIBUICAO.md'),(Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md'),(Join-Path $PublishDir 'licenses') -DestinationPath $zip -Force
$files = @((Join-Path $OutputDir "Mirage-Setup-$version-win-x64.exe"),$zip)
$files | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) } | Set-Content -LiteralPath (Join-Path $OutputDir "SHA256SUMS-$version.txt") -Encoding ascii
$currentNames = @("Mirage-Setup-$version-win-x64.exe", "Mirage-Portable-$version-win-x64.zip", "SHA256SUMS-$version.txt")
if (!$KeepPreviousPackagesForUpgradeTest) { Get-ChildItem -LiteralPath $OutputDir -File | Where-Object {
    $_.Name -notin $currentNames -and ($_.Name -match '^Mirage-(Setup|Portable)-\d+\.\d+\.\d+-win-x64\.(exe|zip)$' -or $_.Name -match '^SHA256SUMS-\d+\.\d+\.\d+\.txt$')
} | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force } }
Write-Output "Distribuição pronta em $OutputDir"
