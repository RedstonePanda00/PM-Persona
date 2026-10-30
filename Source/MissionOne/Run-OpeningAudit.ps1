param([string]$Language='English',[string]$Name='opening-01',[switch]$Odyssey,[switch]$Titles)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$gameRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
$auditRoot=Join-Path $projectRoot ('artifacts\mission-one\'+$Name)
if(Test-Path -LiteralPath $auditRoot){throw 'Choose a fresh audit directory'}
[IO.Directory]::CreateDirectory((Join-Path $auditRoot 'Config')) | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot '.work\UserData17Framework\Config\Prefs.xml') -Destination (Join-Path $auditRoot 'Config\Prefs.xml')
$prefsPath=Join-Path $auditRoot 'Config\Prefs.xml'
$prefs=[IO.File]::ReadAllText($prefsPath)
$prefs=[regex]::Replace($prefs,'<langFolderName>.*?</langFolderName>',('<langFolderName>'+$Language+'</langFolderName>'))
[IO.File]::WriteAllText($prefsPath,$prefs,[Text.UTF8Encoding]::new($false))
@'
<ModsConfigData><version>1.7.4982 rev1280</version><activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>redstonepanda.redstonepandalibrary</li><li>redstonepanda.protossmechcore</li><li>rimcampaign.framework</li><li>redstone.pmpersona</li></activeMods><knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions></ModsConfigData>
'@ | ForEach-Object { if($Odyssey){$_.Replace('<li>ludeon.rimworld.biotech</li><li>redstonepanda','<li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.odyssey</li><li>redstonepanda')}else{$_} } | Set-Content -LiteralPath (Join-Path $auditRoot 'Config\ModsConfig.xml') -Encoding utf8
$gameArguments=@('-quicktest','-pmp-intro-audit',('"-savedatafolder='+$auditRoot+'"'))
if($Titles){$gameArguments+='-pmp-title-audit'}
$process=Start-Process -FilePath (Join-Path $gameRoot 'RimWorld.exe') -WorkingDirectory $gameRoot -ArgumentList $gameArguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $auditRoot 'stdout.log') -RedirectStandardError (Join-Path $auditRoot 'stderr.log') -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $auditRoot 'pid.txt')
Write-Output ('PID='+$process.Id+' audit='+$auditRoot)
