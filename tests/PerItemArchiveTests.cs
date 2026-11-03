using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ScreenLine.Tests {
internal static class PerItemArchiveTests {
static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("通过："+name);}
[STAThread] public static void Main(){
new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
var root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","per-item-"+Guid.NewGuid().ToString("N"),"data");
var store=new StateStore(root);var texts=new TextCollection(store);var first=texts.Create("不归档");var second=texts.Create("归档");
string a="text:"+first.Id,b="text:"+second.Id;
Check(!store.ShouldArchive(a)&&!store.ShouldArchive(b),"未设置条目跟随全局默认关闭");
using(var option=new ArchiveChoice(store,delegate{})){
option.SetKey(a);Check(!option.Toggle.IsChecked.Value&&option.Toggle.IsEnabled,"当前条目显示默认状态");
store.State.ArchiveDeleted=true;store.NotifyArchiveSettings();Check(option.Toggle.IsChecked.Value,"全局变化同步未设置条目");
option.Toggle.IsChecked=false;Check(!store.ShouldArchive(a)&&store.ShouldArchive(b)&&store.State.ArchiveDeleted,"单条关闭不修改全局和其他条目");
option.SetKey(b);Check(option.Toggle.IsChecked.Value,"切换条目显示各自设置");
option.Toggle.IsChecked=false;option.Toggle.IsChecked=true;store.State.ArchiveDeleted=false;store.NotifyArchiveSettings();
Check(store.ShouldArchive(b)&&option.Toggle.IsChecked.Value,"单条开启可覆盖全局关闭");
option.SetKey(a);Check(!option.Toggle.IsChecked.Value,"切回条目保留独立关闭设置");
option.SetKey(null);Check(!option.Toggle.IsEnabled,"没有当前条目时禁止修改");
}
store.Save();var restored=new StateStore(root);Check(!restored.ShouldArchive(a)&&restored.ShouldArchive(b),"单条设置重启后保持");
texts.Remove(first);Check(!Directory.Exists(store.ArchiveRoot),"单条否删除不进入归档");texts.Remove(second);
Check(Directory.GetFiles(store.ArchiveRoot,"*.txt",SearchOption.AllDirectories).Length==1,"单条是即使全局关闭也写入归档");
var third=texts.Create("保存失败保护");var key="text:"+third.Id;
using(var locked=new FileStream(Path.Combine(root,"state.json"),FileMode.Open,FileAccess.Read,FileShare.None)){
bool failed=false;try{store.SetCardArchive(key,true);}catch(IOException){failed=true;}
Check(failed&&!store.ShouldArchive(key),"单条设置保存失败回滚并保持跟随全局");
}
var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,255,255},4);
var path=Path.Combine(root,"pair.png");File.WriteAllBytes(path,ImageContent.EncodeClipboard(bitmap));var gallery=new Gallery(store);var pair=gallery.ImportCard(path,ImageContent.Read(path),"图文详情",true);
store.SetCardArchive(path,true);gallery.DeleteCard(pair);Check(!File.Exists(path)&&Directory.GetFiles(store.ArchiveRoot,"*.png",SearchOption.AllDirectories).Length==1,"图文组合共同遵循当前条目的归档设置");
Console.WriteLine("单条归档测试全部通过");Application.Current.Shutdown();
}
}}
