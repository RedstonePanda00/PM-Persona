param([string]$Language='English',[string]$Name='editor-perf-baseline-01')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$gameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
$auditRoot=Join-Path $projectRoot ('artifacts\mission-two\'+$Name)
if(Test-Path -LiteralPath $auditRoot){throw 'Choose a fresh audit directory'}
[IO.Directory]::CreateDirectory((Join-Path $auditRoot 'Config')) | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot '.work\UserData17Framework\Config\Prefs.xml') -Destination (Join-Path $auditRoot 'Config\Prefs.xml')
$prefsPath=Join-Path $auditRoot 'Config\Prefs.xml'
$prefs=[IO.File]::ReadAllText($prefsPath)
$prefs=[regex]::Replace($prefs,'<langFolderName>.*?</langFolderName>',('<langFolderName>'+$Language+'</langFolderName>'))
[IO.File]::WriteAllText($prefsPath,$prefs,[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $projectRoot 'Source\TestFixtures\Editor\ModsConfig.xml') -Destination (Join-Path $auditRoot 'Config\ModsConfig.xml')
$project=Join-Path $projectRoot 'Campaigns\MissionOne.project.json.gz'
$gameArguments=@('-rc-editor-performance-audit',('"-rc-audit-project='+$project+'"'),('"-savedatafolder='+$auditRoot+'"'))
$process=Start-Process -FilePath (Join-Path $gameRoot 'RimWorld.exe') -WorkingDirectory $gameRoot -ArgumentList $gameArguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $auditRoot 'stdout.log') -RedirectStandardError (Join-Path $auditRoot 'stderr.log') -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $auditRoot 'pid.txt')
Write-Output ('PID='+$process.Id+' audit='+$auditRoot)
