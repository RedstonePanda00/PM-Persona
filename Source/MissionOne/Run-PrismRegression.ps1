param([string]$Name='prism-regression-01',[string]$ConfigSource='Source/TestFixtures/Isolation',[switch]$Vanguard)
$ErrorActionPreference='Stop'
if(Get-CimInstance Win32_Process -Filter "Name='RimWorld.exe'"){throw 'Close running game before audit.'}
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$gameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
$auditRoot=Join-Path $projectRoot ('artifacts\prism-regression\'+$Name)
if(Test-Path -LiteralPath $auditRoot){throw 'Choose a fresh audit directory'}
[IO.Directory]::CreateDirectory((Join-Path $auditRoot 'Config')) | Out-Null
foreach($file in @('Prefs.xml','ModsConfig.xml')){
 Copy-Item -LiteralPath (Join-Path (Join-Path $projectRoot $ConfigSource) $file) -Destination (Join-Path (Join-Path $auditRoot 'Config') $file)
}
$arguments=@('-quicktest','-pmp17prismaudit',('"-savedatafolder='+$auditRoot+'"'))
if($Vanguard){$arguments[1]='-pmp17vanguardaudit'}
$process=Start-Process -FilePath (Join-Path $gameRoot 'RimWorld.exe') -WorkingDirectory $gameRoot -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $auditRoot 'stdout.log') -RedirectStandardError (Join-Path $auditRoot 'stderr.log') -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $auditRoot 'pid.txt')
Write-Output ('PID='+$process.Id+' audit='+$auditRoot)
