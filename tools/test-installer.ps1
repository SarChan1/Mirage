param([string]$SetupPath = '', [string]$ExpectedExecutable = '', [string]$BaselineSetupPath = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$SetupPath) {
    $package = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'dist') -Filter 'Mirage-Setup-*-win-x64.exe' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$package) { throw 'Gere o instalador antes de executar a conferência.' }
    $SetupPath = $package.FullName
}
if (!$ExpectedExecutable) {
    $ExpectedExecutable = Join-Path $projectRoot 'work\release\Mirage.exe'
    if (!(Test-Path -LiteralPath $ExpectedExecutable)) { $ExpectedExecutable = Join-Path $projectRoot 'Mirage.exe' }
}
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{26B6F277-76F2-4C29-9DAB-69D3098536C9}_is1'
if (Test-Path -LiteralPath $uninstallKey) { throw 'Já existe uma instalação registrada. O teste não vai substituí-la.' }
$workRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'work'))
$target = [IO.Path]::GetFullPath((Join-Path $workRoot ('installer-qa-' + [Guid]::NewGuid().ToString('N'))))
if (!$target.StartsWith($workRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Pasta de teste inválida.' }
New-Item -ItemType Directory -Path (Join-Path $target 'WebView2') -Force | Out-Null
$fixturePreferences = Join-Path $target 'preferences.json'
[IO.File]::WriteAllText($fixturePreferences, '{"test":"preserve-existing-user-data"}')
[IO.File]::WriteAllText((Join-Path $target 'WebView2\preserve-test.txt'), 'existing user data')
$fixtureHash = (Get-FileHash -LiteralPath $fixturePreferences -Algorithm SHA256).Hash
$preferencePath = Join-Path $env:LOCALAPPDATA 'Mirage\preferences.json'
$before = if (Test-Path -LiteralPath $preferencePath) { (Get-FileHash -LiteralPath $preferencePath -Algorithm SHA256).Hash } else { '' }
$log = Join-Path $workRoot 'installer-qa-install.log'
$arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="' + $target + '"'),('/LOG="' + $log + '"'))
$firstSetup = if ($BaselineSetupPath) { $BaselineSetupPath } else { $SetupPath }
$process = Start-Process -FilePath $firstSetup -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000)) { throw ('Instalação de teste ainda em andamento: ' + $process.Id) }
$process.Refresh()
if ($process.ExitCode -ne 0) { throw ('Falha no instalador: ' + $process.ExitCode) }
$installed = Join-Path $target 'Mirage.exe'
$previousVersion = (Get-Item -LiteralPath $installed).VersionInfo.ProductVersion.Split('+')[0]
$activeAppUpdateBlocked = $false
if ($BaselineSetupPath) {
    $expectedVersion = (Get-Item -LiteralPath $ExpectedExecutable).VersionInfo.ProductVersion.Split('+')[0]
    if ([version]$previousVersion -ge [version]$expectedVersion) { throw 'O instalador base precisa ser de uma versão anterior.' }
    $baselineHash = (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash
    $mutexCreated = $false
    $sessionMutex = [Threading.Mutex]::new($true, 'Local\Mirage.Desktop.1', [ref]$mutexCreated)
    try {
        if (!$mutexCreated) { throw 'Existe um Mirage aberto. O teste de atualização não vai interferir nessa instância.' }
        $blockedUpdate = Start-Process -FilePath $SetupPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (!$blockedUpdate.WaitForExit(60000)) { throw ('Conferência de bloqueio ainda em andamento: ' + $blockedUpdate.Id) }
        $blockedUpdate.Refresh()
        if ($blockedUpdate.ExitCode -eq 0 -or (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ne $baselineHash) { throw 'A atualização deveria preservar o executável enquanto o Mirage está aberto.' }
        $activeAppUpdateBlocked = $true
        Write-Output 'INSTALLER_ACTIVE_APP_UPDATE_BLOCKED'
    } finally {
        if ($mutexCreated) { $sessionMutex.ReleaseMutex() }
        $sessionMutex.Dispose()
    }
    $process = Start-Process -FilePath $SetupPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000)) { throw ('Atualização de teste ainda em andamento: ' + $process.Id) }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw ('Falha na atualização: ' + $process.ExitCode) }
    if ((Get-FileHash -LiteralPath $fixturePreferences -Algorithm SHA256).Hash -ne $fixtureHash) { throw 'A atualização alterou dados existentes.' }
    Write-Output ('INSTALLER_UPGRADE_OK ' + $previousVersion + ' -> ' + $expectedVersion)
}
if ((Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $ExpectedExecutable -Algorithm SHA256).Hash) { throw 'O aplicativo instalado não corresponde à distribuição.' }
if (!(Test-Path -LiteralPath $uninstallKey)) { throw 'Registro de desinstalação ausente.' }
if (!(Test-Path -LiteralPath (Join-Path $target 'licenses\ludusavi-manifest.txt'))) { throw 'Licença do catálogo ausente.' }
foreach ($license in @('LICENSE','THIRD_PARTY_NOTICES.md','licenses\DotNet-Runtime-NOTICES.txt','licenses\WebView2-LICENSE.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $target $license))) { throw ('Licença ou aviso ausente: ' + $license) }
}
Write-Output 'INSTALLER_INSTALL_OK'
$preview = Join-Path $workRoot 'installer-qa-preview.png'
$process = Start-Process -FilePath $installed -ArgumentList @('--web-smoke-queue',('"' + $preview + '"')) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000)) { throw ('App de teste ainda em andamento: ' + $process.Id) }
$process.Refresh()
if ($process.ExitCode -ne 0) { throw 'O app instalado falhou na conferência da interface.' }
$report = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($preview, '.json')) -Raw | ConvertFrom-Json
if (!$report.ok) { throw 'Conferência do app instalado reprovada.' }
Write-Output ('INSTALLED_APP_UI_OK ' + $report.checks.Count)
$uninstall = Join-Path $target 'unins000.exe'
$uninstallLog = Join-Path $workRoot 'installer-qa-uninstall.log'
$process = Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + $uninstallLog + '"')) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000)) { throw ('Desinstalação de teste ainda em andamento: ' + $process.Id) }
$process.Refresh()
if ($process.ExitCode -ne 0) { throw 'Falha na desinstalação de teste.' }
for ($attempt = 0; $attempt -lt 40 -and (Test-Path -LiteralPath $installed); $attempt++) { Start-Sleep -Milliseconds 250 }
if ((Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $uninstallKey)) { throw 'A desinstalação não removeu os arquivos ou o registro esperado.' }
if ((Get-FileHash -LiteralPath $fixturePreferences -Algorithm SHA256).Hash -ne $fixtureHash -or !(Test-Path -LiteralPath (Join-Path $target 'WebView2\preserve-test.txt'))) { throw 'Dados existentes na pasta de instalação não foram preservados.' }
$after = if (Test-Path -LiteralPath $preferencePath) { (Get-FileHash -LiteralPath $preferencePath -Algorithm SHA256).Hash } else { '' }
if ($before -ne $after) { throw 'As preferências mudaram durante o teste; verificar se houve uma edição na outra instância.' }
[ordered]@{installed=$true;upgradeTested=[bool]$BaselineSetupPath;activeAppUpdateBlocked=$activeAppUpdateBlocked;previousVersion=$previousVersion;installedVersion=(Get-Item -LiteralPath $ExpectedExecutable).VersionInfo.ProductVersion.Split('+')[0];installedAppUiChecks=$report.checks.Count;uninstalled=$true;preferencesPreserved=$true;coLocatedDataPreserved=$true;webViewAlreadyInstalled=$true;missingRuntimeBranchTested=$false;shortcutsCreated=$false;testDirectory=$target;preferencesSha256=$after} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workRoot 'installer-qa-result.json') -Encoding utf8NoBOM
if (!$target.StartsWith($workRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Pasta de teste inválida.' }
for ($attempt = 0; $attempt -lt 40 -and (Test-Path -LiteralPath $uninstall); $attempt++) { Start-Sleep -Milliseconds 250 }
for ($attempt = 0; $attempt -lt 20 -and (Test-Path -LiteralPath $target); $attempt++) {
    try { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop }
    catch [UnauthorizedAccessException] { Start-Sleep -Milliseconds 250 }
}
if (Test-Path -LiteralPath $target) { throw 'A pasta temporária do teste permaneceu bloqueada após a desinstalação.' }
Write-Output 'INSTALLER_UNINSTALL_OK PREFERENCES_PRESERVED'
