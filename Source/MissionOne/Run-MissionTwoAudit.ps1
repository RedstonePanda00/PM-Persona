param([string]$Language='English',[string]$Name='revision-01',[switch]$ObjectivesOnly)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$gameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
$auditRoot=Join-Path $projectRoot ('artifacts\mission-two\'+$Name)
if(Test-Path -LiteralPath $auditRoot){throw 'Choose a fresh audit directory'}
[IO.Directory]::CreateDirectory((Join-Path $auditRoot 'Config')) | Out-Null
$prefs=[IO.File]::ReadAllText((Join-Path $projectRoot '.work\UserData17Framework\Config\Prefs.xml'))
$prefs=[regex]::Replace($prefs,'<langFolderName>.*?</langFolderName>',('<langFolderName>'+$Language+'</langFolderName>'))
$prefs=[regex]::Replace($prefs,'<devMode>.*?</devMode>','<devMode>False</devMode>')
[IO.File]::WriteAllText((Join-Path $auditRoot 'Config\Prefs.xml'),$prefs,[Text.UTF8Encoding]::new($false))
@'
<ModsConfigData><version>1.7.4982 rev1280</version><activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>redstonepanda.redstonepandalibrary</li><li>redstonepanda.protossmechcore</li><li>rimcampaign.framework</li><li>redstone.pmpersona</li></activeMods><knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions></ModsConfigData>
'@ | Set-Content -LiteralPath (Join-Path $auditRoot 'Config\ModsConfig.xml') -Encoding utf8
$arguments=@('-quicktest','-pmp-mission-two-audit',('"-savedatafolder='+$auditRoot+'"'))
if($ObjectivesOnly){$arguments+='-pmp-mission-two-objectives-audit'}
$process=Start-Process -FilePath (Join-Path $gameRoot 'RimWorld.exe') -WorkingDirectory $gameRoot -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $auditRoot 'stdout.log') -RedirectStandardError (Join-Path $auditRoot 'stderr.log') -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $auditRoot 'pid.txt')
Write-Output ('PID='+$process.Id+' audit='+$auditRoot)
