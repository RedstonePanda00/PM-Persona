param([switch]$PlanOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targetRoot = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\PMPersona'
if (Get-Process RimWorld* -ErrorAction SilentlyContinue) { throw 'RimWorld is running; close it before synchronizing runtime files.' }
$metadata = [xml](Get-Content -LiteralPath (Join-Path $targetRoot 'About\About.xml') -Raw)
if ($metadata.ModMetaData.packageId -ine 'redstone.pmpersona') { throw 'Unexpected target mod identity.' }
$files = @(
    'LoadFolders.xml',
    '1.7\Assemblies\PMP_Persona.dll',
    '1.7\Assemblies\PMP.Campaign.dll',
    '1.7\Defs\RecipeDefs\RomulusWeaponUpgrades.xml',
    '1.7\Defs\RecipeDefs\RomulusApparelUpgrades.xml',
    '1.7\Languages\ChineseSimplified\DefInjected\RecipeDef\RomulusWeaponUpgrades.xml',
    '1.7\Languages\ChineseSimplified\DefInjected\RecipeDef\RomulusApparelUpgrades.xml',
    '1.7\Languages\ChineseSimplified\DefInjected\PawnKindDef\Romulus.xml',
    '1.7\Defs\Romulus\BelieverEquipment.xml',
    '1.7\Defs\Romulus\Units.xml',
    '1.7\Defs\Romulus\Bodies.xml',
    '1.7\Defs\Romulus\Doctrine.xml',
    '1.7\Defs\Romulus\CenturionBoss.xml',
    '1.7\Defs\Campaigns\MissionTwo.xml',
    '1.7\Defs\Campaigns\ChampionBoss.xml',
    '1.7\Defs\PawnKindDef\Romulus.xml',
    '1.7\Defs\ThingDefs_Structure\Romulus.xml',
    '1.7\Defs\ThingDefs_Weapon\Absolution.xml',
    '1.7\Defs\ThingDefs_Weapon\Cerberus.xml',
    '1.7\Defs\ThingDefs_Weapon\Opis.xml',
    '1.7\Defs\HediffDefs\Romulus.xml',
    '1.7\Defs\ResearchTabDefs\Romulus.xml',
    '1.7\Defs\ResearchProjectDefs\Romulus.xml',
    '1.7\Languages\ChineseSimplified\DefInjected\ThingDef\BelieverEquipment.xml',
    '1.7\Languages\ChineseSimplified\DefInjected\ThingDef\CenturionEquipment.xml',
    '1.7\Languages\ChineseSimplified\DefInjected\ThingDef\RomulusWeapons.xml',
    '1.7\Languages\ChineseSimplified\DefInjected\ResearchTabDef\Romulus.xml',
    '1.7\Languages\ChineseSimplified\DefInjected\ResearchProjectDef\Romulus.xml',
    'Languages\ChineseSimplified (简体中文)\DefInjected\ThingDef\Campaign.xml'
)
$files += Get-ChildItem -LiteralPath (Join-Path $projectRoot '1.7\Anomaly') -Recurse -File |
    ForEach-Object { [IO.Path]::GetRelativePath($projectRoot, $_.FullName) }
$files = @($files | Sort-Object -Unique)
$plan = foreach ($relative in $files) {
    $source = [IO.Path]::GetFullPath((Join-Path $projectRoot $relative))
    $destination = [IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
    if (-not $source.StartsWith($projectRoot+'\', [StringComparison]::OrdinalIgnoreCase) -or
        -not $destination.StartsWith($targetRoot+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped its deployment root.' }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing source: $relative" }
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $previousHash = if (Test-Path -LiteralPath $destination -PathType Leaf) { (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash } else { $null }
    [pscustomobject]@{ RelativePath=$relative; Source=$source; Destination=$destination; SourceHash=$sourceHash; PreviousHash=$previousHash; Changed=($sourceHash -ne $previousHash) }
}
if ($PlanOnly) {
    $plan | Select-Object RelativePath,Changed | Format-Table -AutoSize
    Write-Output ("Plan: {0} files, {1} changed." -f $plan.Count,@($plan | Where-Object Changed).Count)
    return
}
$backupRoot = Join-Path $projectRoot ('artifacts\romulus-research-deploy\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-ffff'))
[IO.Directory]::CreateDirectory($backupRoot) | Out-Null
$plan | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupRoot 'manifest.json') -Encoding utf8
foreach ($entry in $plan | Where-Object Changed) {
    if ($entry.PreviousHash) {
        $backup = Join-Path $backupRoot $entry.RelativePath
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
        Copy-Item -LiteralPath $entry.Destination -Destination $backup
        if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $entry.PreviousHash) { throw "Backup verification failed: $($entry.RelativePath)" }
    }
}
foreach ($entry in $plan | Where-Object Changed) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($entry.Destination)) | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force
}
foreach ($entry in $plan) {
    if ((Get-FileHash -LiteralPath $entry.Destination -Algorithm SHA256).Hash -ne $entry.SourceHash) { throw "Deployment verification failed: $($entry.RelativePath)" }
}
Write-Output ("Verified {0} files; updated {1}." -f $plan.Count,@($plan | Where-Object Changed).Count)
Write-Output ("Backup: "+$backupRoot)
Write-Output ("Target: "+$targetRoot)
