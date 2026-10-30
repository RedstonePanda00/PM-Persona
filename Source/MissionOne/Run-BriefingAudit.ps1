param([switch]$Third,[switch]$Deploy,[int]$Width=1280,[int]$Height=720)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$game='C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
$target=Join-Path $game 'Mods/PMPersona'
if(Get-Process RimWorld* -ErrorAction SilentlyContinue){throw 'RimWorld is running'}
$chapter=if($Third){'three'}else{'two'}
$run=Join-Path $root ('artifacts/briefing-fullscreen/'+$chapter+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory((Join-Path $run 'Config')) | Out-Null
if($Deploy){
 foreach($file in @('1.7/Assemblies/PMP.Campaign.dll','1.7/Shaders/PMP17_CampaignTerrain.frag.glsl')){
  $dest=Join-Path $target $file;$saved=Join-Path $run ('before/'+$file)
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
  Copy-Item -LiteralPath $dest -Destination $saved
  Copy-Item -LiteralPath (Join-Path $root $file) -Destination $dest -Force
 }
 Copy-Item -LiteralPath (Join-Path $root 'artifacts/briefing-fullscreen/audit-bin/PMP.BriefingAudit.dll') -Destination (Join-Path $target '1.7/Assemblies/PMP.BriefingAudit.dll') -Force
}
foreach($file in @('ModsConfig.xml','Prefs.xml')){Copy-Item -LiteralPath (Join-Path $root "Source/TestFixtures/Isolation/$file") -Destination (Join-Path $run "Config/$file")}
$prefsPath=Join-Path $run 'Config/Prefs.xml'
$prefs=[IO.File]::ReadAllText($prefsPath)
$prefs=[regex]::Replace($prefs,'<screenWidth>.*?</screenWidth>',"<screenWidth>$Width</screenWidth>")
$prefs=[regex]::Replace($prefs,'<screenHeight>.*?</screenHeight>',"<screenHeight>$Height</screenHeight>")
[IO.File]::WriteAllText($prefsPath,$prefs,[Text.UTF8Encoding]::new($false))
$arguments=@('-quicktest','-pmp-briefing-audit',('"-savedatafolder='+$run+'"'))
if($Third){$arguments+='-pmp-briefing-third'}
$proc=Start-Process -FilePath (Join-Path $game 'RimWorld.exe') -WorkingDirectory $game -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
$run | Set-Content (Join-Path $root ('artifacts/briefing-fullscreen/latest-'+$chapter+'.txt'))
Write-Output "PID $($proc.Id): $run"
