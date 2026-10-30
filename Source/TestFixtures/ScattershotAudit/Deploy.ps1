param([switch]$Audit)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$target='C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\PMPersona'
if(Get-Process RimWorld* -ErrorAction SilentlyContinue){throw 'RimWorld is running'}
$backup=Join-Path $root ('artifacts/scattershot/deploy-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$files=@('1.7/Defs/ThingDefs_Weapon/Scattershot.xml','1.7/Defs/GunSmith/Scattershot.xml','1.7/Defs/GunSmith/HardlightRifle.xml','1.7/Patches/HardlightCommonAttachments.xml','Textures/Things/Weapon/Scattershot/Scattershot.png','Languages/ChineseSimplified (简体中文)/DefInjected/ThingDef/Scattershot.xml','Languages/ChineseSimplified (简体中文)/DefInjected/GunSmithFramework.GunSmithPlatformDef/Scattershot.xml')
$files+=@('1.7/Assemblies/PMP_Persona.dll','Textures/Things/Weapon/Scattershot/Scattershot_DualGrip_Icon.png','Languages/ChineseSimplified (简体中文)/DefInjected/GunSmithFramework.GunSmithPartDef/Scattershot.xml')
$manifest=@()
foreach($file in $files){
 $src=Join-Path $root $file;$dest=[IO.Path]::GetFullPath((Join-Path $target $file))
 if(-not $dest.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid deployment target'}
 if(Test-Path -LiteralPath $dest){$saved=Join-Path $backup $file;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved))|Out-Null;Copy-Item -LiteralPath $dest -Destination $saved}
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest))|Out-Null;Copy-Item -LiteralPath $src -Destination $dest -Force
 $hash=(Get-FileHash -LiteralPath $src).Hash;if($hash -ne (Get-FileHash -LiteralPath $dest).Hash){throw "Hash mismatch: $file"}
 $manifest+=@{File=$file;SHA256=$hash}
}
$manifest|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts/scattershot/deployed-manifest.json') -Encoding utf8
if($Audit){Copy-Item -LiteralPath (Join-Path $root 'artifacts/scattershot/audit-bin/PMP.ScattershotAudit.dll') -Destination (Join-Path $target '1.7/Assemblies/PMP.ScattershotAudit.dll') -Force}
Write-Output 'Scattershot deployed with matching hashes.'
