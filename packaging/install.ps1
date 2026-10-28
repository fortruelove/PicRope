$ErrorActionPreference = 'Stop'
$logPath = Join-Path $PSScriptRoot 'logs\latest.log'
New-Item -ItemType Directory -Path (Split-Path $logPath -Parent) -Force | Out-Null
Set-Content -LiteralPath $logPath -Value ('Installer started: ' + [DateTime]::Now.ToString('s')) -Encoding UTF8
try {
$installPath = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PicRope'))
$sourcePath = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'PicRope.exe'))) { throw '安装文件不完整，请先完整解压安装包。' }
$framework = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
if ($framework.Release -lt 528040) { throw '请先安装 .NET Framework 4.8。' }
$running = @(Get-Process -Name PicRope -ErrorAction SilentlyContinue)
if ($running.Count) { throw '请先从托盘退出图绳，再重新安装。' }
if ((Test-Path -LiteralPath $installPath) -and ((Get-Item -LiteralPath $installPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '安装目录不能是目录链接。' }
New-Item -ItemType Directory -Path $installPath -Force | Out-Null
Get-ChildItem -LiteralPath $sourcePath -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $installPath -Recurse -Force
}
$exePath = Join-Path $installPath 'PicRope.exe'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'PicRope.lnk'))
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installPath
$shortcut.IconLocation = $exePath + ',0'
$shortcut.Description = '图绳：把截图挂在屏幕上沿，随手取用。'
$shortcut.Save()
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PicRope'
New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'PicRope · 图绳' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value ([Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion) -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installPath -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value ($exePath + ',0') -PropertyType String -Force | Out-Null
$uninstallCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installPath 'uninstall.ps1') + '"'
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
Add-Content -LiteralPath $logPath -Value 'Installation completed.' -Encoding UTF8
}
catch {
    Add-Content -LiteralPath $logPath -Value ($_ | Out-String) -Encoding UTF8
    Write-Host ('安装失败。详情见安装包目录中的 logs\latest.log。')
    throw
}

# Installation is complete. A declined or blocked launch must not undo or misreport it.
Write-Output '图绳安装已完成，桌面已创建 PicRope 快捷方式。'
try {
    # Launch the interactive application normally so Windows can show its confirmation UI.
    Start-Process -FilePath $exePath -WorkingDirectory $installPath
    try { Add-Content -LiteralPath $logPath -Value 'Application launch requested.' -Encoding UTF8 }
    catch { Write-Warning '程序启动请求已提交，但安装日志写入失败。' }
}
catch {
    $launchError = $_
    $nativeCode = $null
    $exception = $_.Exception
    while ($exception) {
        if ($exception -is [ComponentModel.Win32Exception]) { $nativeCode = $exception.NativeErrorCode; break }
        $exception = $exception.InnerException
    }
    Add-Content -LiteralPath $logPath -Value ('Application launch failed. NativeErrorCode=' + $nativeCode + [Environment]::NewLine + ($launchError | Out-String)) -Encoding UTF8
    if ($nativeCode -eq 1223) {
        Write-Warning '安装已完成，但 Windows 返回启动操作已取消。可稍后双击桌面 PicRope 快捷方式启动；若出现系统提示，请查看提示内容并决定是否允许运行。'
    }
    else {
        Write-Warning '安装已完成，但程序未能自动启动。请尝试双击桌面 PicRope 快捷方式；启动错误已记录到安装包目录中的 logs\latest.log。'
    }
}
