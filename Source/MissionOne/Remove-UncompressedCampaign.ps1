param([Parameter(Mandatory)][string]$TargetRoot,[Parameter(Mandatory)][string]$BackupRoot)
$ErrorActionPreference='Stop'
$modRoot=[IO.Path]::GetFullPath($TargetRoot).TrimEnd('\')
$backupRootPath=[IO.Path]::GetFullPath($BackupRoot).TrimEnd('\')
if($backupRootPath.StartsWith($modRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Backups must be outside the installed mod'}
$relative='1.7/Campaigns/MissionOne.json'
$legacy=[IO.Path]::GetFullPath((Join-Path $modRoot $relative))
$packed=$legacy+'.gz'
if(-not $legacy.StartsWith($modRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Campaign path escaped mod root'}
$source=Join-Path $PSScriptRoot '../../1.7/Campaigns/MissionOne.json.gz'
if(-not(Test-Path -LiteralPath $packed)){throw 'Compressed campaign has not been deployed'}
if((Get-FileHash -LiteralPath $packed).Hash -ne (Get-FileHash -LiteralPath $source).Hash){throw 'Compressed campaign deployment hash mismatch'}
$definition=Join-Path $modRoot '1.7/Defs/Campaigns/MissionOne.xml'
$xml=[xml][IO.File]::ReadAllText($definition)
if($xml.Defs.'RimCampaign.Runtime.CampaignDef'.projectFile -ne 'Campaigns/MissionOne.json.gz'){throw 'Campaign definition still points at the uncompressed file'}
if(Test-Path -LiteralPath $legacy){
    $saved=[IO.Path]::GetFullPath((Join-Path $backupRootPath $relative))
    if(-not $saved.StartsWith($backupRootPath+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Backup path escaped root'}
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
    if(Test-Path -LiteralPath $saved){throw 'Legacy campaign backup already exists'}
    Copy-Item -LiteralPath $legacy -Destination $saved
    if((Get-FileHash -LiteralPath $legacy).Hash -ne (Get-FileHash -LiteralPath $saved).Hash){throw 'Legacy campaign backup hash mismatch'}
    Remove-Item -LiteralPath $legacy
}
