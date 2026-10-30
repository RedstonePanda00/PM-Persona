param([string]$Name=('native-'+(Get-Date -Format 'yyyyMMdd-HHmmss')))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$game='C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
$target=Join-Path $game 'Mods/PMPersona'
if(Get-Process RimWorld* -ErrorAction SilentlyContinue){throw 'Close running RimWorld before audit.'}
$run=Join-Path $root ('artifacts/ir-shield-fix/'+$Name)
if(Test-Path -LiteralPath $run){throw 'Choose a fresh audit name.'}
[IO.Directory]::CreateDirectory((Join-Path $run 'Config'))|Out-Null
foreach($file in @('ModsConfig.xml','Prefs.xml')){Copy-Item -LiteralPath (Join-Path $root "Source/TestFixtures/Isolation/$file") -Destination (Join-Path $run "Config/$file")}
$auditDll=Join-Path $target '1.7/Assemblies/PMP.OrbitalSensorAudit.dll'
if(Test-Path -LiteralPath $auditDll){throw 'An orbital sensor audit DLL is already installed.'}
try {
 Copy-Item -LiteralPath (Join-Path $root 'artifacts/ir-shield-fix/audit-bin/PMP.OrbitalSensorAudit.dll') -Destination $auditDll
 $process=Start-Process -FilePath (Join-Path $game 'RimWorld.exe') -WorkingDirectory $game -ArgumentList @('-quicktest','-pmp-orbital-sensor-audit',('"-savedatafolder='+$run+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
 Write-Output ('PID='+$process.Id+' audit='+$run)
 $process.WaitForExit()
 if(-not (Test-Path -LiteralPath (Join-Path $run 'OrbitalSensorAudit/PASS.txt'))){throw ('Audit failed: '+$run)}
 Get-Content -LiteralPath (Join-Path $run 'OrbitalSensorAudit/PASS.txt')
} finally {
 if(Test-Path -LiteralPath $auditDll){Remove-Item -LiteralPath $auditDll}
}
