$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$targetRoot='C:/Program Files (x86)/Steam/steamapps/common/RimWorld/Mods/PMPersona'
$targetRoot=[IO.Path]::GetFullPath($targetRoot)
$backupRoot=Join-Path $projectRoot ('artifacts/tactical-sprint/deploy-backup-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))
$files=@('1.7/Assemblies/PMP_Persona.dll','1.7/Patches/TacticalSprint.xml','1.7/Defs/PawnKindDef/PawnKind_GestaltZero.xml','Languages/English/Keyed/TacticalSprint.xml','Languages/ChineseSimplified (简体中文)/Keyed/TacticalSprint.xml','Textures/UI/Commands/TacticalSprint.png')
foreach($relative in $files){
 $source=Join-Path $projectRoot $relative
 $destination=[IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
 if(-not $destination.StartsWith($targetRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid deploy destination'}
 if(Test-Path -LiteralPath $destination){
  if((Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $destination).Hash){continue}
  $backup=Join-Path $backupRoot $relative
  New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($backup)) -Force | Out-Null
  Copy-Item -LiteralPath $destination -Destination $backup
 }
 New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
 Copy-Item -LiteralPath $source -Destination $destination -Force
}
foreach($relative in @('1.7/Defs/AbilityDefs/AbilityDef_GestaltZero.xml','Languages/ChineseSimplified (简体中文)/DefInjected/AbilityDef/AbilityDef_GestaltZero.xml')){
 $obsolete=[IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
 $backup=[IO.Path]::GetFullPath((Join-Path $backupRoot ('retired/'+$relative)))
 if(-not $obsolete.StartsWith($targetRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or -not $backup.StartsWith($projectRoot+'\artifacts\tactical-sprint\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid retirement path'}
 if(Test-Path -LiteralPath $obsolete){
  New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($backup)) -Force | Out-Null
  Move-Item -LiteralPath $obsolete -Destination $backup
 }
}
Write-Output ('Tactical sprint deployed to '+$targetRoot)
