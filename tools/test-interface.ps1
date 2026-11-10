param(
    [string]$Executable = '',
    [ValidateSet('batch-small','running-wide','recovery-small','endurance','locale-pt','locale-en','locale-es','save-center','save-center-en','save-center-es')]
    [string[]]$Scenes = @('batch-small','running-wide','recovery-small','endurance')
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$Executable) { $Executable = Join-Path $projectRoot 'work\release\Mirage.exe' }
$Executable = (Resolve-Path -LiteralPath $Executable).ProviderPath
$output = Join-Path $projectRoot 'work'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$reports = @()
foreach ($scene in $Scenes) {
    $image = Join-Path $output ('interface-qa-' + $scene + '.png')
    $reportPath = [IO.Path]::ChangeExtension($image, '.json')
    if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath -Force }
    $process = Start-Process -FilePath $Executable -ArgumentList @('--web-smoke-' + $scene, ('"' + $image + '"')) -WindowStyle Hidden -PassThru
    Write-Output ('Conferindo ' + $scene + ' (prévia isolada, PID ' + $process.Id + ')')
    if (!$process.WaitForExit(180000)) {
        $process.Refresh()
        if (!$process.HasExited -and $process.Path -eq $Executable) { Stop-Process -Id $process.Id -Force }
        throw ('A prévia excedeu o tempo esperado: ' + $scene)
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw ('A prévia falhou: ' + $scene + '. Confira os arquivos de diagnóstico em work.') }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (!$report.ok -or !$report.native.singleWindow) { throw ('Relatório incompleto: ' + $scene) }
    if ($scene -eq 'running-wide' -and !$report.runtime.ok) { throw 'Conferência de sessão reprovada.' }
    if ($scene -eq 'recovery-small' -and $report.recovery.checks.Count -lt 10) { throw 'Conferência de recuperação incompleta.' }
    if ($scene -eq 'endurance' -and $report.background.extendedSampleSeconds -ne 60) { throw 'Medição estendida incompleta.' }
    $reports += @{ scene=$scene; report=$report }
    Write-Output ('Interface aprovada: ' + $scene + ' (' + $report.checks.Count + ' verificações)')
}
$reports | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $output 'interface-qa-result.json') -Encoding utf8NoBOM
