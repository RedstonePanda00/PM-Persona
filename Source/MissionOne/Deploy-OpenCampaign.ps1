param([switch]$Audit)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$game='C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
if(Get-Process RimWorld* -ErrorAction SilentlyContinue){throw 'RimWorld is running; do not replace loaded assemblies.'}
& (Join-Path $root '.work/RimCampaign-stealth/artifacts/Deploy-Stealth.ps1')
$target=Join-Path $game 'Mods/PMPersona'
$backup=Join-Path $root ('artifacts/open-campaign/deploy-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$files=@('1.7/Assemblies/PMP_Persona.dll','1.7/Assemblies/PMP.Campaign.dll','1.7/Campaigns/MissionOne.json.gz','1.7/Defs/Campaigns/MissionOne.xml','1.7/Defs/Scenarios/Scenarios.xml','Languages/English/Keyed/OpenCampaign.xml','Languages/ChineseSimplified (简体中文)/Keyed/OpenCampaign.xml','Languages/ChineseSimplified (简体中文)/DefInjected/ScenarioDef/Campaign.xml','Languages/ChineseSimplified (简体中文)/DefInjected/ScenarioDef/Scenarios.xml','Languages/ChineseSimplified (简体中文)/DefInjected/RimCampaign.Runtime.CampaignDef/Campaign.xml')
$files+=@('LoadFolders.xml','1.7/Ideology/Assemblies/PMP.Campaign.Ideology.dll','1.7/Ideology/Languages/English/Keyed/CampaignDefaults.xml','1.7/Ideology/Languages/ChineseSimplified (简体中文)/Keyed/CampaignDefaults.xml')
foreach($file in $files){
 $source=Join-Path $root $file;$dest=[IO.Path]::GetFullPath((Join-Path $target $file))
 if(-not $dest.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid deployment target'}
 if(Test-Path -LiteralPath $dest){
  if((Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $dest).Hash){continue}
  $saved=Join-Path $backup $file;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
  Copy-Item -LiteralPath $dest -Destination $saved
 }
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest)) | Out-Null
 Copy-Item -LiteralPath $source -Destination $dest -Force
 if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $dest).Hash){throw "Deployment mismatch: $file"}
}
foreach($legacy in @('Languages/English/Keyed/CampaignDefaults.xml','Languages/ChineseSimplified (简体中文)/Keyed/CampaignDefaults.xml')) {
 $old=Join-Path $target $legacy
 if(Test-Path -LiteralPath $old) {
  $saved=Join-Path $backup $legacy;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
  Copy-Item -LiteralPath $old -Destination $saved
  Remove-Item -LiteralPath $old
 }
}
if($Audit){Copy-Item -LiteralPath (Join-Path $root 'artifacts/open-campaign/audit-bin/PMP.OpenCampaignAudit.dll') -Destination (Join-Path $target '1.7/Assemblies/PMP.OpenCampaignAudit.dll') -Force}
& (Join-Path $PSScriptRoot 'Remove-UncompressedCampaign.ps1') -TargetRoot $target -BackupRoot $backup
'Open campaign deployed and hashes verified.'
