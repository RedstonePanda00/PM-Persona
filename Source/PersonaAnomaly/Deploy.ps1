param([switch]$Audit)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$target='C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\PMPersona'
$assembly=Join-Path $root '1.7/Anomaly/Assemblies/PMP.Anomaly.dll'
if(-not (Test-Path -LiteralPath $assembly)){throw 'Stasis DLL is missing or intercepted. Rebuild and validate it before enabling the Anomaly load folder.'}
Get-FileHash -LiteralPath $assembly | Out-Null
if(Get-Process RimWorld* -ErrorAction SilentlyContinue){throw 'RimWorld is running; do not replace loaded assemblies.'}
$backup=Join-Path $root ('artifacts/stasis/deploy-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$files=@('LoadFolders.xml')+@(Get-ChildItem -LiteralPath (Join-Path $root '1.7/Anomaly') -Recurse -File | ForEach-Object {$_.FullName.Substring($root.Length+1)})
foreach($file in $files){
 $src=Join-Path $root $file;$dst=[IO.Path]::GetFullPath((Join-Path $target $file))
 if(-not $dst.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid target'}
 if(Test-Path -LiteralPath $dst){
  if((Get-FileHash -LiteralPath $src).Hash -eq (Get-FileHash -LiteralPath $dst).Hash){continue}
  $saved=Join-Path $backup $file;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved))|Out-Null;Copy-Item -LiteralPath $dst -Destination $saved
 }
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dst))|Out-Null;Copy-Item -LiteralPath $src -Destination $dst -Force
 if((Get-FileHash -LiteralPath $src).Hash -ne (Get-FileHash -LiteralPath $dst).Hash){throw "Hash mismatch: $file"}
}
if($Audit){Copy-Item -LiteralPath (Join-Path $root 'artifacts/stasis/audit-bin/PMP.StasisAudit.dll') -Destination (Join-Path $target '1.7/Assemblies/PMP.StasisAudit.dll') -Force}
'Stasis content deployed; hashes verified.'
