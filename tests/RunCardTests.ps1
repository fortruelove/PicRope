$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Net.Http.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','Microsoft.VisualBasic.dll') | ForEach-Object { '/r:'+(Join-Path $framework $_) }
$references+=@('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/r:'+(Join-Path $framework ('WPF\'+$_)) }
$references+=@('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll') | ForEach-Object { '/r:'+(Join-Path $root ('vendor\WebView2\'+$_)) }
$sources=(Get-ChildItem (Join-Path $root 'src') -Filter '*.cs').FullName
$resources=@('/resource:'+(Join-Path $root 'src\assets\PicRope.ico')+',PicRope.Icon')
$resources+=Get-ChildItem (Join-Path $root 'build\runtime-assets') -Filter '*.png' | ForEach-Object { '/resource:'+$_.FullName+',PicRope.'+ $(if($_.BaseName.StartsWith('basket-')){'Basket.'+$_.BaseName.Substring(7)}else{'Envelope.'+$_.BaseName.Substring(9)}) }
foreach($name in @('CardTests','ClipboardCardTests','CardUiTests','CardIntegrationTests','ClipboardWorkerTests')) {
  if(!(Test-Path (Join-Path $PSScriptRoot ($name+'.cs')))) {continue}
  $output=Join-Path $root ('build\'+$name+'.exe')
  & (Join-Path $framework 'csc.exe') /nologo /codepage:65001 /platform:x64 /target:exe ('/out:'+$output) ('/main:ScreenLine.Tests.'+$name) $resources $references $sources (Join-Path $PSScriptRoot ($name+'.cs'))
  if($LASTEXITCODE -ne 0){throw 'Card test compilation failed'}
  & $output
  if($LASTEXITCODE -ne 0){throw 'Card tests failed'}
}
