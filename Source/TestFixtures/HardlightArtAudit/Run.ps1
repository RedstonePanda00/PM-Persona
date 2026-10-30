param([string]$Name='native')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$game='C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
if(Get-Process RimWorld* -ErrorAction SilentlyContinue){throw 'RimWorld is running'}
$run=Join-Path $root ('artifacts/hardlight-layout/'+$Name+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory((Join-Path $run 'Config'))|Out-Null
foreach($file in @('ModsConfig.xml','Prefs.xml')){Copy-Item -LiteralPath (Join-Path $root "Source/TestFixtures/Isolation/$file") -Destination (Join-Path $run "Config/$file")}
$proc=Start-Process -FilePath (Join-Path $game 'RimWorld.exe') -WorkingDirectory $game -ArgumentList @('-quicktest','-pmp-hardlight-layout-audit',('"-savedatafolder='+$run+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
$run|Set-Content (Join-Path $root 'artifacts/hardlight-layout/latest-audit.txt')
Write-Output "PID $($proc.Id): $run"


