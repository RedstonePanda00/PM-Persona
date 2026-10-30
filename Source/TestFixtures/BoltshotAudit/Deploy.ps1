param([switch]$Audit)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$target='C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\PMPersona'
if(Get-Process RimWorld* -ErrorAction SilentlyContinue){throw 'RimWorld is running'}
$backup=Join-Path $root ('artifacts/boltshot/deploy-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$files=@('About/About.xml','1.7/Assemblies/PMP_Persona.dll','1.7/Defs/ThingDefs_Weapon/Boltshot.xml','1.7/Defs/GunSmith/Boltshot.xml','1.7/Patches/HardlightCommonAttachments.xml','Textures/Things/Weapon/Boltshot/Boltshot.png','Textures/UI/Campaign/RomulusInsignia.png','Textures/Things/Weapon/HardlightRifle/Hardlight_Optic.png','Textures/Things/Weapon/HardlightRifle/Hardlight_UnderbarrelMount.png','Languages/ChineseSimplified (简体中文)/DefInjected/ThingDef/Boltshot.xml','Languages/ChineseSimplified (简体中文)/DefInjected/GunSmithFramework.GunSmithPlatformDef/Boltshot.xml','Languages/ChineseSimplified (简体中文)/DefInjected/GunSmithFramework.GunSmithPartDef/Boltshot.xml','Languages/ChineseSimplified (简体中文)/DefInjected/GunSmithFramework.GunSmithSlotDef/Boltshot.xml')
$files+=@('1.7/Defs/ThingDefs_Weapon/Prometheus.xml','1.7/Defs/GunSmith/HardlightRifle.xml','Textures/Things/Weapon/HardlightRifle/Hardlight_Body.png','Textures/Things/Weapon/Boltshot/Boltshot_DualGrip_Icon.png')
$manifest=@()
foreach($file in $files){
 $src=Join-Path $root $file;$dest=[IO.Path]::GetFullPath((Join-Path $target $file))
 if(-not $dest.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid deployment target'}
 if(Test-Path -LiteralPath $dest){$saved=Join-Path $backup $file;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved))|Out-Null;Copy-Item -LiteralPath $dest -Destination $saved}
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest))|Out-Null;Copy-Item -LiteralPath $src -Destination $dest -Force
 $hash=(Get-FileHash -LiteralPath $src).Hash;if($hash -ne (Get-FileHash -LiteralPath $dest).Hash){throw "Hash mismatch: $file"}
 $manifest+=@{File=$file;SHA256=$hash}
}
$obsolete=Join-Path $target 'Textures/Things/Weapon/HardlightRifle/Hardlight_Preview.png'
if(Test-Path -LiteralPath $obsolete){
 $saved=Join-Path $backup 'Hardlight_Preview.png';[IO.Directory]::CreateDirectory($backup)|Out-Null;Copy-Item -LiteralPath $obsolete -Destination $saved
 Remove-Item -LiteralPath $obsolete
}
$manifest|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts/boltshot/deployed-manifest.json') -Encoding utf8
if($Audit){Copy-Item -LiteralPath (Join-Path $root 'artifacts/boltshot/audit-bin/PMP.BoltshotAudit.dll') -Destination (Join-Path $target '1.7/Assemblies/PMP.BoltshotAudit.dll') -Force}
Write-Output 'Boltshot and Romulus mod icon deployed with matching hashes.'
