param([string]$Name='audit-01')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$auditRoot=Join-Path $projectRoot ('artifacts/tactical-sprint/'+$Name)
if(Test-Path -LiteralPath $auditRoot){throw 'Choose a fresh audit directory'}
New-Item -ItemType Directory -Path (Join-Path $auditRoot 'Config') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'Source/TestFixtures/TacticalSprint/Prefs.xml') -Destination (Join-Path $auditRoot 'Config/Prefs.xml')
Copy-Item -LiteralPath (Join-Path $projectRoot 'Source/TestFixtures/TacticalSprint/ModsConfig.xml') -Destination (Join-Path $auditRoot 'Config/ModsConfig.xml')
$gameRoot='C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
$arguments=@('-quicktest','-pmp-tactical-sprint-audit',('"-savedatafolder='+$auditRoot+'"'))
$process=Start-Process -FilePath (Join-Path $gameRoot 'RimWorld.exe') -WorkingDirectory $gameRoot -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $auditRoot 'stdout.log') -RedirectStandardError (Join-Path $auditRoot 'stderr.log') -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $auditRoot 'pid.txt')
Write-Output ('PID='+$process.Id+' audit='+$auditRoot)
