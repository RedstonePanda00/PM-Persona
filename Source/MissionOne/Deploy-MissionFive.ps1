param([switch]$Audit)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$game='C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
if(Get-Process RimWorld* -ErrorAction SilentlyContinue){throw 'RimWorld is running; do not replace loaded assemblies.'}
# This chapter adds no framework code. Refuse to deploy against a different framework build.
$framework=Join-Path $root '.work/RimCampaign-stealth/1.7'
foreach($relative in @('Assemblies/RimCampaign.Core.dll','Assemblies/RimCampaign.Runtime.dll','Assemblies/RimCampaign.SDK.dll','Integrations/ProtossMech/Assemblies/RimCampaign.Integration.ProtossMech.dll')){
 $local=Join-Path $framework $relative;$installed=Join-Path $game ('Mods/RimCampaign/1.7/'+$relative)
 if((Get-FileHash -LiteralPath $local).Hash -ne (Get-FileHash -LiteralPath $installed).Hash){throw "Framework version mismatch: $relative"}
}
$target=Join-Path $game 'Mods/PMPersona'
$backup=Join-Path $root ('artifacts/mission-five/deploy-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$files=@('1.7/Assemblies/PMP.Campaign.dll','1.7/Campaigns/MissionOne.json.gz','1.7/Defs/Campaigns/MissionOne.xml','1.7/Defs/Campaigns/MissionFiveWarpBeacon.xml','1.7/Shaders/PMP17_CampaignTerrain.frag.glsl','Languages/English/Keyed/MissionFive.xml','Languages/ChineseSimplified (简体中文)/Keyed/MissionFive.xml')
$files+=@('1.7/Assemblies/PMP_Persona.dll','1.7/Defs/PrometheusWarp.xml','1.7/Defs/AbilityDefs/Prometheus.xml','1.7/Defs/ThingDefs_Structure/FleetBeacon.xml','1.7/Defs/ThingDefs_Structure/Building.xml','Textures/Things/Structure/FleetBeacon.png','Textures/Things/Structure/PurifierConclave.png','Languages/ChineseSimplified (简体中文)/DefInjected/AbilityDef/Prometheus.xml','Languages/ChineseSimplified (简体中文)/DefInjected/ProtossMech.WarpReinforcementDef/Prometheus.xml')
foreach($file in $files){
 $source=Join-Path $root $file;$dest=[IO.Path]::GetFullPath((Join-Path $target $file))
 if(-not $dest.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid deployment target'}
 if(Test-Path -LiteralPath $dest){
  if((Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $dest).Hash){continue}
  $saved=Join-Path $backup $file;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null;Copy-Item -LiteralPath $dest -Destination $saved
 }
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest)) | Out-Null;Copy-Item -LiteralPath $source -Destination $dest -Force
}
foreach($file in @('1.7/Defs/Campaigns/MissionFive.xml','Languages/ChineseSimplified (简体中文)/DefInjected/WorkGiverDef/MissionFive.xml')){
 $dest=[IO.Path]::GetFullPath((Join-Path $target $file))
 if(-not $dest.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid obsolete file target'}
 if(Test-Path -LiteralPath $dest){
  $saved=Join-Path $backup $file;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
  Copy-Item -LiteralPath $dest -Destination $saved
  Remove-Item -LiteralPath $dest
 }
}
if($Audit){Copy-Item -LiteralPath (Join-Path $root 'artifacts/mission-five/audit-bin/PMP.MissionFiveAudit.dll') -Destination (Join-Path $target '1.7/Assemblies/PMP.MissionFiveAudit.dll') -Force}
& (Join-Path $PSScriptRoot 'Remove-UncompressedCampaign.ps1') -TargetRoot $target -BackupRoot $backup
Write-Output 'Mission five deployed. Framework and previous chapter data preserved.'
