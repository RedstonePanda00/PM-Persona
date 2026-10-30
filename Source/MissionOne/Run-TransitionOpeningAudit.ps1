param([string]$Name='transition-opening-01',[string]$Language='ChineseSimplified (简体中文)',[switch]$Downed,[switch]$Inside)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$gameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
$auditRoot=Join-Path $projectRoot ('artifacts\mission-two\'+$Name)
if(Test-Path -LiteralPath $auditRoot){throw 'Choose a fresh audit directory'}
[IO.Directory]::CreateDirectory((Join-Path $auditRoot 'Config')) | Out-Null
$source=Join-Path $projectRoot 'Source\TestFixtures\Isolation'
Copy-Item -LiteralPath (Join-Path $source 'ModsConfig.xml') -Destination (Join-Path $auditRoot 'Config\ModsConfig.xml')
$prefs=[IO.File]::ReadAllText((Join-Path $source 'Prefs.xml'))
$prefs=[regex]::Replace($prefs,'<langFolderName>.*?</langFolderName>',('<langFolderName>'+$Language+'</langFolderName>'))
[IO.File]::WriteAllText((Join-Path $auditRoot 'Config\Prefs.xml'),$prefs,[Text.UTF8Encoding]::new($false))
$arguments=@('-quicktest','-pmp-transition-opening-audit',('"-savedatafolder='+$auditRoot+'"'))
if($Downed){$arguments+='-pmp-opening-downed-audit'}
if($Inside){$arguments+='-pmp-forge-inside-audit'}
$process=Start-Process -FilePath (Join-Path $gameRoot 'RimWorld.exe') -WorkingDirectory $gameRoot -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $auditRoot 'stdout.log') -RedirectStandardError (Join-Path $auditRoot 'stderr.log') -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $auditRoot 'pid.txt')
Write-Output ('PID='+$process.Id+' audit='+$auditRoot)
