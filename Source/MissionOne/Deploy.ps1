$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
& (Join-Path $projectRoot '.work\RimCampaign-stealth\artifacts\Deploy-Stealth.ps1')
$modsRoot='C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods'
$targetRoot=Join-Path $modsRoot 'PMPersona'
$backupRoot=Join-Path $projectRoot ('artifacts\mission-one\deploy-backup-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))
$languageFiles=Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Languages') -File -Recurse | Where-Object {$_.Name -in @('Campaign.xml','CampaignText.xml','CampaignResults.xml','MissionThreeTroops.xml')} | ForEach-Object {[IO.Path]::GetRelativePath($projectRoot,$_.FullName)}
$missionTwoFiles=@('Textures\UI\Campaign\RomulusInsignia.png','Textures\Things\Building\Romulus\Obelisk.png','Textures\Things\Building\Romulus\Dome.png','Textures\Things\Building\Romulus\Hub.png','1.7\Defs\ShaderDefs\CampaignBriefing.xml','1.7\Shaders\PMP17_CampaignTerrain.frag.glsl','1.7\Shaders\PMP17_RomulusRune.frag.glsl','1.7\Defs\Campaigns\RomulusTurret.xml','Textures\Things\Building\Romulus\SpearTurret_Base.png','Textures\Things\Building\Romulus\SpearTurret_Top.png','1.7\Defs\Campaigns\ChampionBoss.xml','1.7\Patches\ChampionBoss.xml')
$languageFiles+=Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Languages') -Filter 'ChampionBoss.xml' -File -Recurse | ForEach-Object {[IO.Path]::GetRelativePath($projectRoot,$_.FullName)}
# These shared definitions were extracted from Campaigns/MissionTwo.xml.
$romulusDefinitionFiles=@('1.7\Defs\PawnKindDef\Romulus.xml','1.7\Defs\ThingDefs_Structure\Romulus.xml','1.7\Defs\ThingDefs_Weapon\Absolution.xml','1.7\Defs\HediffDefs\Romulus.xml')
$romulusDefinitionFiles+=@('1.7\Defs\ThingDefs_Weapon\Cerberus.xml','1.7\Defs\ThingDefs_Weapon\Opis.xml','1.7\Languages\ChineseSimplified\DefInjected\ThingDef\RomulusWeapons.xml')
$romulusDefinitionFiles+=@('1.7\Defs\ResearchTabDefs\Romulus.xml','1.7\Defs\ResearchProjectDefs\Romulus.xml','1.7\Languages\ChineseSimplified\DefInjected\ResearchTabDef\Romulus.xml','1.7\Languages\ChineseSimplified\DefInjected\ResearchProjectDef\Romulus.xml')
foreach($relative in @('1.7\Defs\Romulus\Units.xml','1.7\Defs\Campaigns\MissionThreeTroops.xml','1.7\Patches\MissionThreeWeaponTags.xml','1.7\Assemblies\PMP_Persona.dll','1.7\Defs\Campaigns\MissionThree.xml','1.7\Assemblies\PMP.Campaign.dll','1.7\Campaigns\MissionOne.json.gz','1.7\Defs\Campaigns\MissionOne.xml','1.7\Defs\Campaigns\MissionTwo.xml','Textures\UI\Campaign\StarEatersInsignia.png','Textures\Things\Building\Linked\RomulusWall_Atlas.png','Textures\Things\Building\Linked\RomulusWall_MenuIcon.png')+$missionTwoFiles+$romulusDefinitionFiles+$languageFiles){
 $source=Join-Path $projectRoot $relative
 $destination=[IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
 if(-not $destination.StartsWith($targetRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid destination'}
 if(Test-Path -LiteralPath $destination){
  if((Get-FileHash -LiteralPath $destination).Hash -eq (Get-FileHash -LiteralPath $source).Hash){continue}
  $backup=Join-Path $backupRoot $relative
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
  Copy-Item -LiteralPath $destination -Destination $backup
 }
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
 Copy-Item -LiteralPath $source -Destination $destination -Force
}
# Retire only this task's superseded victory shader files, preserving originals.
foreach($relative in @('Languages\English\DefInjected\CampaignDef\Campaign.xml','1.7\Defs\ShaderDefs\CampaignVictory.xml','1.7\Shaders\PMP17_CampaignVictory.vert.glsl','1.7\Shaders\PMP17_CampaignVictory.frag.glsl')){
 $obsolete=[IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
 $saved=[IO.Path]::GetFullPath((Join-Path $backupRoot ('retired-victory\'+$relative)))
 if(-not $obsolete.StartsWith($targetRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or -not $saved.StartsWith($backupRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid superseded victory path'}
 if(Test-Path -LiteralPath $obsolete){
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
  Move-Item -LiteralPath $obsolete -Destination $saved
 }
}
$emptyLegacyLanguage=[IO.Path]::GetFullPath((Join-Path $targetRoot 'Languages\English\DefInjected\CampaignDef'))
if($emptyLegacyLanguage.StartsWith($targetRoot+'\',[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $emptyLegacyLanguage) -and @(Get-ChildItem -LiteralPath $emptyLegacyLanguage -Force).Count -eq 0){Remove-Item -LiteralPath $emptyLegacyLanguage}
# Retire the former standalone mission only after the embedded files are in place.
$retired=[IO.Path]::GetFullPath((Join-Path $modsRoot 'PMPMissionOne'))
$retiredBackup=[IO.Path]::GetFullPath((Join-Path $backupRoot 'retired-PMPMissionOne'))
if($retired -ne ($modsRoot+'\PMPMissionOne') -or -not $retiredBackup.StartsWith($projectRoot+'\artifacts\mission-one\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid retirement paths'}
if(Test-Path -LiteralPath $retired){
 $metadata=[xml](Get-Content -LiteralPath (Join-Path $retired 'About\About.xml') -Raw)
 if($metadata.ModMetaData.packageId -ne 'redstone.pmp.campaign'){throw 'Unexpected standalone mod owner'}
 [IO.Directory]::CreateDirectory($backupRoot) | Out-Null
 Move-Item -LiteralPath $retired -Destination $retiredBackup
}
$config=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) '..\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml'
if(Test-Path -LiteralPath $config){
 $text=[IO.File]::ReadAllText($config)
 $updated=[regex]::Replace($text,'(?im)^[ \t]*<li>redstone\.pmp\.campaign</li>\r?\n','')
 if($updated -cne $text){
  [IO.Directory]::CreateDirectory($backupRoot) | Out-Null
  Copy-Item -LiteralPath $config -Destination (Join-Path $backupRoot 'ModsConfig.before.xml')
  [IO.File]::WriteAllText($config,$updated,[Text.UTF8Encoding]::new($false))
 }
}
& (Join-Path $PSScriptRoot 'Remove-UncompressedCampaign.ps1') -TargetRoot $targetRoot -BackupRoot $backupRoot
Write-Output ('Campaign deployed inside Persona: '+$targetRoot)
