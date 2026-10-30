param([switch]$Audit)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$wild='C:\Users\15858\OneDrive\Documents\Sundry\RimworldMod\ProtossMechCore'
$mods='C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods'
if(Get-Process RimWorld* -ErrorAction SilentlyContinue){throw 'RimWorld is running'}
$backup=Join-Path $root ('artifacts/stasis/marker-deploy-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$sets=@(@{Root=$wild;Mod='ProtossMechCore';Files=@('1.7/Assemblies/ProtossMech.dll')+@(Get-ChildItem (Join-Path $wild '1.7/Defs/ThingDefs_Structure') -Filter '*.xml'|ForEach-Object {$_.FullName.Substring($wild.Length+1)})},@{Root=$root;Mod='PMPersona';Files=@('1.7/Defs/ThingDefs_Structure/Building.xml','1.7/Defs/ThingDefs_Structure/FleetBeacon.xml')})
foreach($set in $sets){foreach($file in $set.Files){
 $src=Join-Path $set.Root $file;$dst=Join-Path (Join-Path $mods $set.Mod) $file
 if((Test-Path -LiteralPath $dst)-and (Get-FileHash -LiteralPath $src).Hash -eq (Get-FileHash -LiteralPath $dst).Hash){continue}
 if(Test-Path -LiteralPath $dst){$saved=Join-Path (Join-Path $backup $set.Mod) $file;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved))|Out-Null;Copy-Item -LiteralPath $dst -Destination $saved}
 Copy-Item -LiteralPath $src -Destination $dst -Force
 if((Get-FileHash -LiteralPath $src).Hash -ne (Get-FileHash -LiteralPath $dst).Hash){throw "Hash mismatch: $file"}
}}
if($Audit){Copy-Item -LiteralPath (Join-Path $root 'artifacts/open-campaign/audit-bin/PMP.OpenCampaignAudit.dll') -Destination (Join-Path $mods 'PMPersona/1.7/Assemblies/PMP.OpenCampaignAudit.dll') -Force}
'Protoss marker deployed and hashes verified.'
