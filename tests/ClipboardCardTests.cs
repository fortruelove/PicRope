using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
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
var mixed=new ClipboardCard {Text="关联备注",Picture=new ClipboardPicture {Bytes=ImageContent.EncodeClipboard(image),Extension=".png"}};
if(CardClipboard.ForAutomatic(mixed,true,false).Text!=null || CardClipboard.ForAutomatic(mixed,false,true).Picture!=null || CardClipboard.ForAutomatic(mixed,false,false)!=null || CardClipboard.ForAutomatic(mixed,true,true).Text!="关联备注")throw new Exception("自动获取遵循两类独立开关");
var external=new DataObject();external.SetImage(image);
card=CardClipboard.ReadData(new FaultyData(external,DataFormats.UnicodeText,DataFormats.Text),true).GetAwaiter().GetResult();
if(card.Picture==null)throw new Exception("文本格式无效不能阻断图片粘贴");
var fallback=new DataObject();fallback.SetData("PNG",new MemoryStream(new byte[]{1,2,3}));fallback.SetImage(image);
if(CardClipboard.ReadData(fallback,true).GetAwaiter().GetResult().Picture==null)throw new Exception("损坏PNG应继续尝试位图");
byte[] dib=new byte[44];BitConverter.GetBytes(40).CopyTo(dib,0);BitConverter.GetBytes(1).CopyTo(dib,4);BitConverter.GetBytes(1).CopyTo(dib,8);dib[12]=1;dib[14]=24;dib[40]=10;dib[41]=20;dib[42]=30;
var dibData=new DataObject();dibData.SetData(DataFormats.Dib,new MemoryStream(dib));
if(CardClipboard.ReadData(dibData,true).GetAwaiter().GetResult().Picture==null)throw new Exception("支持DIB原生位图");
var v5=new byte[128];BitConverter.GetBytes(124).CopyTo(v5,0);BitConverter.GetBytes(1).CopyTo(v5,4);BitConverter.GetBytes(1).CopyTo(v5,8);v5[12]=1;v5[14]=24;v5[124]=40;v5[125]=50;v5[126]=60;var native=new DataObject();native.SetData("Format17",new MemoryStream(v5));
if(CardClipboard.ReadData(new FaultyData(native,DataFormats.Bitmap),true).GetAwaiter().GetResult().Picture==null)throw new Exception("Bitmap转换失败应回退DIBV5");
var mime=new DataObject();mime.SetData("image/png",new MemoryStream(ImageContent.EncodeClipboard(image)));if(CardClipboard.ReadData(new FaultyData(mime,"PNG"),true).GetAwaiter().GetResult().Picture==null)throw new Exception("PNG格式错误应回退微信MIME格式");
var missing=new DataObject();missing.SetText("有效文字");missing.SetData(DataFormats.FileDrop,new[]{Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".png")});
if(CardClipboard.ReadData(missing,true).GetAwaiter().GetResult().Text!="有效文字")throw new Exception("失效图片文件不能丢掉有效文字");
var file=Path.Combine(Path.GetTempPath(),"PicRope-test-"+Guid.NewGuid().ToString("N")+".png");File.WriteAllBytes(file,ImageContent.EncodeClipboard(image));
try{var files=new DataObject(DataFormats.FileDrop,new[]{file});if(CardClipboard.ReadData(files,false).GetAwaiter().GetResult().Picture==null)throw new Exception("自动获取支持复制图片文件");}finally{File.Delete(file);}
Console.WriteLine("剪贴板卡片测试：12 通过，0 失败");
}
}
internal sealed class FaultyData : IDataObject {
readonly IDataObject inner;readonly string[] bad;
public FaultyData(IDataObject inner,params string[] bad){this.inner=inner;this.bad=bad;}
public object GetData(string format,bool convert){if(Array.IndexOf(bad,format)>=0)throw new COMException("无效格式",unchecked((int)0x800401D3));return inner.GetData(format,convert);}
public object GetData(string format){return GetData(format,true);}public object GetData(Type type){return inner.GetData(type);}
public bool GetDataPresent(string f,bool c){return Array.IndexOf(bad,f)>=0 || inner.GetDataPresent(f,c);}public bool GetDataPresent(string f){return GetDataPresent(f,true);}public bool GetDataPresent(Type t){return inner.GetDataPresent(t);}
public string[] GetFormats(){return inner.GetFormats();}public string[] GetFormats(bool c){return inner.GetFormats(c);}
public void SetData(string f,object v,bool c){inner.SetData(f,v,c);}public void SetData(string f,object v){inner.SetData(f,v);}public void SetData(Type t,object v){inner.SetData(t,v);}public void SetData(object v){inner.SetData(v);}
}}
