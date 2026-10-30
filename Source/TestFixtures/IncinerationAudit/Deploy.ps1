param([switch]$Audit)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$target='C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\PMPersona'
if(Get-Process RimWorld* -ErrorAction SilentlyContinue){throw 'RimWorld is running'}
$backup=Join-Path $root ('artifacts/incineration-cannon/deploy-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$files=@('1.7/Defs/ThingDefs_Weapon/IncinerationCannon.xml','1.7/Defs/GunSmith/IncinerationCannon.xml','1.7/Defs/GunSmith/HardlightRifle.xml','1.7/Patches/HardlightCommonAttachments.xml','Textures/Things/Weapon/IncinerationCannon/IncinerationCannon.png','Languages/ChineseSimplified (简体中文)/DefInjected/ThingDef/IncinerationCannon.xml','Languages/ChineseSimplified (简体中文)/DefInjected/GunSmithFramework.GunSmithPlatformDef/IncinerationCannon.xml')
$files+=@('1.7/Assemblies/PMP_Persona.dll')
$manifest=@()
foreach($file in $files){
 $src=Join-Path $root $file;$dest=[IO.Path]::GetFullPath((Join-Path $target $file))
 if(-not $dest.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid deployment target'}
 if(Test-Path -LiteralPath $dest){$saved=Join-Path $backup $file;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved))|Out-Null;Copy-Item -LiteralPath $dest -Destination $saved}
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest))|Out-Null;Copy-Item -LiteralPath $src -Destination $dest -Force
 $hash=(Get-FileHash -LiteralPath $src).Hash;if($hash -ne (Get-FileHash -LiteralPath $dest).Hash){throw "Hash mismatch: $file"}
 $manifest+=@{File=$file;SHA256=$hash}
}
$manifest|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts/incineration-cannon/deployed-manifest.json') -Encoding utf8
if($Audit){Copy-Item -LiteralPath (Join-Path $root 'artifacts/incineration-cannon/audit-bin/PMP.IncinerationAudit.dll') -Destination (Join-Path $target '1.7/Assemblies/PMP.IncinerationAudit.dll') -Force}
Write-Output 'IncinerationCannon deployed with matching hashes.'


