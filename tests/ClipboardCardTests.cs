using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ScreenLine.Tests {
internal static class ClipboardCardTests {
[STAThread] public static void Main() {
var data=new DataObject(); var pixels=new byte[16];
var image=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,pixels,8);
data.SetImage(image); data.SetText("备注\n  全文");
var card=CardClipboard.ReadData(data,false).GetAwaiter().GetResult();
if(card.Picture==null || card.Text!="备注\n  全文") throw new Exception("图文必须来自同一快照");
data.SetData(ClipboardRules.OwnFormat,"1");
if(CardClipboard.ReadData(data,false).GetAwaiter().GetResult()!=null) throw new Exception("自动获取应忽略自身复制");
if(CardClipboard.ReadData(data,true).GetAwaiter().GetResult().Picture==null) throw new Exception("手动粘贴应允许自身复制");
var text=new DataObject(); text.SetText("只有文字");
card=CardClipboard.ReadData(text,true).GetAwaiter().GetResult();
if(card.Picture!=null || card.Text!="只有文字") throw new Exception("文字独立保存");
Console.WriteLine("剪贴板卡片测试：4 通过，0 失败");
}
}}
