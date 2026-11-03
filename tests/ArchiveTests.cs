using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ScreenLine.Tests {
internal static class ArchiveTests {
static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("通过："+name);}
[STAThread] public static void Main(){
var root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","archive-"+Guid.NewGuid().ToString("N"),"data");
var store=new StateStore(root);var texts=new TextCollection(store);var gallery=new Gallery(store);
Check(!store.State.ArchiveDeleted,"归档默认关闭");
var plain=texts.Create("永久删除");texts.Remove(plain);Check(!texts.Items.Contains(plain)&&!Directory.Exists(store.ArchiveRoot),"关闭时文字永久删除且不创建归档");
store.State.ArchiveDeleted=true;store.Save();
var date=new DateTime(2025,3,4,5,6,7,DateTimeKind.Utc);var local=date.ToLocalTime();
var text=texts.Create("中文\n完整文字");text.CreatedUtc=date;texts.UpdateContent(text,"修改后的\n完整文字");texts.Remove(text);
var directory=Path.Combine(store.ArchiveRoot,local.ToString("yyyy"),local.ToString("MM"));var stem=local.ToString("yyyy-MM-dd_HH-mm-ss");
var monthly=Path.Combine(directory,local.ToString("yyyy-MM")+".txt");
Check(File.ReadAllText(monthly,Encoding.UTF8).Contains("任务ID："+stem+Environment.NewLine+"任务详情："+Environment.NewLine+"修改后的\n完整文字"),"当月文字包含任务ID及完整详情");
var duplicate=texts.Create("同秒");duplicate.CreatedUtc=date;texts.Remove(duplicate);
Check(File.ReadAllText(monthly).Contains("任务ID："+stem+"_1"+Environment.NewLine),"同秒重名使用序号不覆盖");
var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,255,255},4);
var image=Path.Combine(root,"image.png");File.WriteAllBytes(image,ImageContent.EncodeClipboard(bitmap));var card=gallery.ImportCard(image,ImageContent.Read(image),"配图备注",true);
store.State.CardCreatedUtc[image]=date;store.Save();gallery.DeleteCard(card);
Check(File.Exists(Path.Combine(directory,stem+"_2.png"))&&File.ReadAllText(monthly).Contains("任务ID："+stem+"_2"+Environment.NewLine+"任务详情："+Environment.NewLine+"配图备注")&&!File.Exists(image),"图文使用相同名称配对且移除原内容");
Check(new StateStore(root).State.ArchiveDeleted&&new StateStore(root).State.CardCreatedUtc[image]==date,"开关和初次创建时间持久保存");
var batch1=texts.Create("批量一");var batch2=texts.Create("批量二");batch1.CreatedUtc=batch2.CreatedUtc=date;
texts.RemoveMany(new[]{batch1,batch2});Check(!texts.Items.Contains(batch1)&&!texts.Items.Contains(batch2)&&File.ReadAllText(monthly).Contains("任务ID："+stem+"_3"+Environment.NewLine)&&File.ReadAllText(monthly).Contains("任务ID："+stem+"_4"+Environment.NewLine),"批量删除逐条归档且不互相覆盖");
Check(Directory.GetFiles(directory,"*.txt").Length==1&&File.ReadAllText(monthly).Split(new[]{"-----------------------------------"},StringSplitOptions.None).Length==5,"同月只生成一个文本文件且任务间使用指定分隔线");
var legacyFile=Path.Combine(directory,stem+"_5.txt");File.WriteAllText(legacyFile,"旧版归档保持原样");
var legacyMemo=texts.Create("新格式");legacyMemo.CreatedUtc=date;texts.Remove(legacyMemo);
Check(File.ReadAllText(legacyFile)=="旧版归档保持原样"&&File.ReadAllText(monthly).Contains("任务ID："+stem+"_6"+Environment.NewLine),"兼容已有归档并避开旧文件ID");
using(var locked=new FileStream(monthly,FileMode.Open,FileAccess.Read,FileShare.None)) {
var retry=texts.Create("月文件被占用");retry.CreatedUtc=date;bool lockedFailed=false;try{texts.Remove(retry);}catch(IOException){lockedFailed=true;}
Check(lockedFailed&&texts.Items.Contains(retry),"月文本文件无法读取时保留备忘");
}
var soloPath=Path.Combine(root,"solo.png");File.WriteAllBytes(soloPath,ImageContent.EncodeClipboard(bitmap));var solo=gallery.ImportCard(soloPath,ImageContent.Read(soloPath),"",true);
var firstCreated=store.CardCreated(soloPath);File.SetLastWriteTimeUtc(soloPath,DateTime.UtcNow.AddDays(1));gallery.Remove(solo);gallery.HangCard(solo);
Check(store.CardCreated(soloPath)==firstCreated&&new StateStore(root).CardCreated(soloPath).ToString("s")==firstCreated.ToString("s"),"编辑文件及取下重挂不改变图片初次创建时间");
gallery.DeleteCard(solo);Check(Directory.GetFiles(store.ArchiveRoot,"*.png",SearchOption.AllDirectories).Length==2,"纯图片单独归档");
var blockedRoot=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(root)),"blocked","data");var blocked=new StateStore(blockedRoot);var blockedTexts=new TextCollection(blocked);var keep=blockedTexts.Create("不能丢失");blocked.State.ArchiveDeleted=true;
File.WriteAllText(blocked.ArchiveRoot,"blocked");bool failed=false;try{blockedTexts.Remove(keep);}catch(IOException){failed=true;}
Check(failed&&blockedTexts.Items.Contains(keep),"归档失败保留文字记录");
var image2=Path.Combine(blockedRoot,"keep.png");File.WriteAllBytes(image2,ImageContent.EncodeClipboard(bitmap));var bg=new Gallery(blocked);var keepImage=bg.ImportCard(image2,ImageContent.Read(image2),"保留备注",true);failed=false;try{bg.DeleteCard(keepImage);}catch(IOException){failed=true;}
Check(failed&&File.Exists(image2)&&bg.Collected.Contains(keepImage)&&bg.Note(keepImage)=="保留备注","归档失败保留图片及备注");
store.State.ArchiveDeleted=false;var image3=Path.Combine(root,"delete.png");File.WriteAllBytes(image3,ImageContent.EncodeClipboard(bitmap));var gone=gallery.ImportCard(image3,ImageContent.Read(image3),"",true);int count=Directory.GetFiles(store.ArchiveRoot,"*",SearchOption.AllDirectories).Length;gallery.DeleteCard(gone);
Check(!File.Exists(image3)&&Directory.GetFiles(store.ArchiveRoot,"*",SearchOption.AllDirectories).Length==count,"关闭时图片永久删除且不产生归档");
Console.WriteLine("归档测试全部通过");
}
}}
