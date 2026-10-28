$ErrorActionPreference='Stop'
$installTarget=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\PicRope'))
$expectedTarget=[IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PicRope'))
if($installTarget -ne $expectedTarget){throw 'Unexpected installation directory'}
$executable=Join-Path $installTarget 'PicRope.exe'
if(Test-Path $executable){
  Start-Process -FilePath $executable -ArgumentList '--quit' -WindowStyle Hidden -Wait
  for($attempt=0;$attempt -lt 20;$attempt++){
    $running=@(Get-Process PicRope -ErrorAction SilentlyContinue)
    if($running.Count -eq 0){break}
    Start-Sleep -Milliseconds 250
  }
  if(Get-Process PicRope -ErrorAction SilentlyContinue){throw 'PicRope is still running. Close any unsaved editor before upgrading.'}
}
$backupRoot=Join-Path (Split-Path $PSScriptRoot -Parent) 'local-backups'
$backup=Join-Path $backupRoot ('PicRope-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -ErrorAction Stop | Out-Null
if(Test-Path $installTarget){Copy-Item -LiteralPath $installTarget -Destination (Join-Path $backup 'installation') -Recurse}
$stateFile=Join-Path $installTarget 'data\state.json'
$before=if(Test-Path $stateFile){(Get-FileHash $stateFile -Algorithm SHA256).Hash}else{$null}
& (Join-Path $PSScriptRoot 'build\package\install.ps1')
if($before -and (Get-FileHash $stateFile -Algorithm SHA256).Hash -ne $before){throw 'Data changed during install; backup retained'}
Start-Sleep -Seconds 2
$process=Get-Process PicRope -ErrorAction Stop
if(!$process.Responding){throw 'PicRope is not responding'}
if((Get-FileHash $executable).Hash -ne (Get-FileHash (Join-Path $PSScriptRoot 'build\PicRope.exe')).Hash){throw 'Installed executable differs from verified build'}
Write-Output ('Backup: '+$backup)
Write-Output ('Installed: '+$executable)
Write-Output ('Version: '+[Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion)
Write-Output ('Running and responding: '+$process.Id)
