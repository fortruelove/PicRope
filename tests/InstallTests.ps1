param([string]$InstallerPath = (Join-Path $PSScriptRoot '..\packaging\install.ps1'))
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP ('picrope-install-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
# OS integration is mocked; file copying and user-data preservation use real files.
$module = New-Module -ArgumentList $root,$InstallerPath -ScriptBlock {
    param($TestRoot,$Installer)
    $global:PicRopeTestRoot=$TestRoot
    $script:Installer=[IO.Path]::GetFullPath($Installer)
    $global:PicRopeTestRunning=$false
    $global:PicRopeTestLaunchError=0
    $global:PicRopeTestLocalVersion='0.0.0.0'
    $global:PicRopeTestAnswer='Y'
    $global:PicRopeTestPrompts=0
    $global:PicRopeTestQuits=0
    $global:PicRopeTestQuitBlocked=$false
    function Read-Host { param($Prompt) $global:PicRopeTestPrompts++; return $global:PicRopeTestAnswer }
    function Get-Item {
        param($LiteralPath,[switch]$Force)
        if ($LiteralPath -eq (Microsoft.PowerShell.Management\Join-Path $global:PicRopeTestRoot 'installed\PicRope.exe')) {
            return [pscustomobject]@{VersionInfo=[pscustomobject]@{FileVersion=$global:PicRopeTestLocalVersion}}
        }
        Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force:$Force
    }
    function Get-CimInstance { throw 'WMI access denied' }
    function Get-Process { param($Name,$ErrorAction) if ($global:PicRopeTestRunning) { [pscustomobject]@{ProcessName='PicRope'} } }
    function Join-Path {
        param($Path,$ChildPath)
        if ($ChildPath -eq 'Programs\PicRope') { return Microsoft.PowerShell.Management\Join-Path $global:PicRopeTestRoot 'installed' }
        Microsoft.PowerShell.Management\Join-Path $Path $ChildPath
    }
    function New-Item {
        param($Path,$ItemType,[switch]$Force)
        if ($Path -like 'HKCU:*') { return }
        Microsoft.PowerShell.Management\New-Item -Path $Path -ItemType $ItemType -Force:$Force
    }
    function New-ItemProperty { param($Path,$Name,$Value,$PropertyType,[switch]$Force) }
    function New-Object {
        param($ComObject)
        if ($ComObject -ne 'WScript.Shell') { throw 'Unexpected COM object' }
        $shell=[pscustomobject]@{}
        $shell | Add-Member ScriptMethod CreateShortcut {
            param($Path)
            $shortcut=[pscustomobject]@{TargetPath='';WorkingDirectory='';IconLocation='';Description=''}
            $shortcut | Add-Member ScriptMethod Save { }
            return $shortcut
        }
        return $shell
    }
    function Start-Process {
        param($FilePath,$WorkingDirectory,$WindowStyle,$ArgumentList,[switch]$PassThru)
        if (!(Test-Path -LiteralPath $FilePath)) { throw 'Missing installed executable' }
        if ($ArgumentList -eq '--quit') {
            $global:PicRopeTestQuits++
            if (!$global:PicRopeTestQuitBlocked) { $global:PicRopeTestRunning=$false }
            $result=[pscustomobject]@{}
            $result | Add-Member ScriptMethod WaitForExit { param($Milliseconds) return !$global:PicRopeTestQuitBlocked }
            return $result
        }
        if ($global:PicRopeTestLaunchError) {
            throw [InvalidOperationException]::new('Launch failed', [ComponentModel.Win32Exception]::new($global:PicRopeTestLaunchError))
        }
    }
    function Invoke-Checks {
        $target=Join-Path $global:PicRopeTestRoot 'installed'
        New-Item -ItemType Directory -Path (Join-Path $target 'data') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $target 'data\state.json'),'preserve-settings')
        & $script:Installer
        if (!(Test-Path -LiteralPath (Join-Path $target 'PicRope.exe'))) { throw 'Install did not copy executable' }
        if ([IO.File]::ReadAllText((Join-Path $target 'data\state.json')) -ne 'preserve-settings') { throw 'User data changed' }
        if (!(Test-Path -LiteralPath (Join-Path (Split-Path $script:Installer) 'logs\latest.log'))) { throw 'Missing installer log' }
        foreach ($code in @(1223,5)) {
            $global:PicRopeTestLaunchError=$code
            # Copying and registration must remain successful when only launching fails.
            $result = (& $script:Installer 3>&1 | Out-String)
            $log = Get-Content -LiteralPath (Join-Path (Split-Path $script:Installer) 'logs\latest.log') -Raw
            if ($log -notmatch 'Installation completed\.' -or $log -notmatch ('NativeErrorCode=' + $code)) { throw 'Missing installation/launch outcome in log' }
            if ($result -notmatch '安装已完成' -or $result -notmatch '桌面' -or $result -match '安装失败') { throw 'Launch failure was reported as installation failure' }
            if ($code -eq 1223 -and $result -notmatch '取消') { throw 'Missing cancellation explanation' }
            if ([IO.File]::ReadAllText((Join-Path $target 'data\state.json')) -ne 'preserve-settings') { throw 'Launch failure changed user data' }
        }
        $global:PicRopeTestLaunchError=0
        $global:PicRopeTestRunning=$true
        & $script:Installer
        if ($global:PicRopeTestRunning -or $global:PicRopeTestQuits -ne 1 -or $global:PicRopeTestPrompts -ne 0) { throw 'Upgrade did not gracefully close running application without prompt' }
        $packageVersion=(Microsoft.PowerShell.Management\Get-Item -LiteralPath (Join-Path (Split-Path $script:Installer) 'app\PicRope.exe')).VersionInfo.FileVersion
        foreach ($version in @($packageVersion,'99.0.0.0')) {
            $global:PicRopeTestLocalVersion=$version
            $global:PicRopeTestRunning=$true
            $global:PicRopeTestAnswer='N'
            $quits=$global:PicRopeTestQuits
            $before=(Get-FileHash -LiteralPath (Join-Path $target 'PicRope.exe')).Hash
            & $script:Installer
            if (!$global:PicRopeTestRunning -or $global:PicRopeTestQuits -ne $quits -or (Get-FileHash -LiteralPath (Join-Path $target 'PicRope.exe')).Hash -ne $before) { throw 'Declining reinstall/downgrade changed app or files' }
            $global:PicRopeTestAnswer='Y'
            & $script:Installer
            if ($global:PicRopeTestRunning -or $global:PicRopeTestQuits -ne ($quits+1)) { throw 'Confirmed reinstall/downgrade did not close and continue' }
        }
        $global:PicRopeTestRunning=$false
        $global:PicRopeTestAnswer='N'
        $quits=$global:PicRopeTestQuits
        & $script:Installer
        if ($global:PicRopeTestQuits -ne $quits) { throw 'Cancelled stopped-app reinstall requested exit' }
        $global:PicRopeTestAnswer='Y'
        $global:PicRopeTestRunning=$true
        $global:PicRopeTestQuitBlocked=$true
        $blocked=$false
        try { & $script:Installer } catch { $blocked=$_.Exception.Message -match '退出请求未完成' }
        if (!$blocked -or !$global:PicRopeTestRunning) { throw 'Failed graceful exit did not block installation' }
        if ($global:PicRopeTestPrompts -ne 6) { throw 'Same/lower version confirmation was not consistently shown' }
        Write-Output 'Installer checks passed: copy/data preservation, launch errors, automatic upgrade shutdown, equal/lower version confirmation, cancellation, and blocked graceful exit.'
    }
}
& $module { Invoke-Checks }
