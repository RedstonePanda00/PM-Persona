param([switch]$Audit)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$game='C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
if(Get-Process RimWorld -ErrorAction SilentlyContinue){throw 'RimWorld is running; do not replace loaded assemblies.'}
& (Join-Path $root '.work/RimCampaign-stealth/artifacts/Deploy-Stealth.ps1')
$target=Join-Path $game 'Mods/PMPersona'
$backup=Join-Path $root ('artifacts/mission-four/deploy-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$files=@('1.7/Assemblies/PMP.Campaign.dll','1.7/Defs/Campaigns/MissionFour.xml','1.7/Defs/Campaigns/MissionOne.xml','1.7/Campaigns/MissionOne.json.gz')
$files+=Get-ChildItem (Join-Path $root 'Languages') -Recurse -File -Filter 'MissionFour.xml' | ForEach-Object {[IO.Path]::GetRelativePath($root,$_.FullName)}
foreach($file in $files){
 $source=Join-Path $root $file;$dest=[IO.Path]::GetFullPath((Join-Path $target $file))
 if(-not $dest.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid deployment target'}
 if(Test-Path -LiteralPath $dest){
  if((Get-FileHash $source).Hash -eq (Get-FileHash $dest).Hash){continue}
  $saved=Join-Path $backup $file;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null;Copy-Item -LiteralPath $dest -Destination $saved
 }
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest)) | Out-Null;Copy-Item -LiteralPath $source -Destination $dest -Force
}
if($Audit){Copy-Item -LiteralPath (Join-Path $root 'artifacts/mission-four/audit-bin/PMP.MissionFourAudit.dll') -Destination (Join-Path $target '1.7/Assemblies/PMP.MissionFourAudit.dll') -Force}
& (Join-Path $PSScriptRoot 'Remove-UncompressedCampaign.ps1') -TargetRoot $target -BackupRoot $backup
Write-Output 'Mission four deployed; all previous chapter map values preserved.'
