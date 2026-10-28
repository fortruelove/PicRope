using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ScreenLine.Tests {
internal static class CardTests {
static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("通过：" + name); }
[STAThread] public static void Main() {
var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "cards-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var store = new StateStore(root); var texts = new TextCollection(store); var gallery = new Gallery(store);
Check(!store.State.AutoClipboard && texts.AutoHang && store.State.AutoHang, "自动获取默认关闭，两类自动上绳默认开启");
var text = texts.Add("完整备注\n  中文缩进"); Check(text.Hanging && gallery.Hanging.Count == 1, "文字默认挂绳");
texts.SetHanging(new[] {text}, false); Check(gallery.Hanging.Count == 0, "文字取下保留原文");
texts.SetHanging(new[] {text}, true);
var pixels = new byte[16]; for(int i=3;i<16;i+=4) pixels[i]=255;
var bitmap = BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,pixels,8);
var path = Path.Combine(root,"one.png"); File.WriteAllBytes(path,ImageContent.EncodeClipboard(bitmap));
gallery.Collect(path,ImageContent.Read(path)); var pic = gallery.Collected[0];
gallery.AttachText(pic,text,true,false);
Check(texts.Items.Count == 0 && gallery.Note(pic)=="完整备注\n  中文缩进" && gallery.Hanging.Count==1, "从文字配图合并且沿用挂绳状态");
var restored = new StateStore(root); Check(restored.State.ImageNotes[path].Contains("缩进") && restored.State.Images.Contains(path), "组合卡片重启恢复");
gallery.Split(pic,texts);
Check(texts.Items.Count==1 && gallery.Note(pic)=="" && texts.Items[0].Hanging, "拆分保留图片和完整文字");
texts.AutoHang=false; var second=texts.Add("不自动上绳"); Check(!second.Hanging,"关闭文字自动上绳仅保存");
gallery.SetNote(pic,"原备注"); gallery.AttachText(pic,second,false,true);
Check(gallery.Note(pic)=="原备注\n不自动上绳" && texts.Items.Count==1,"追加关联不覆盖已有备注且不产生副本");
gallery.RemovePicture(pic,texts); Check(!gallery.Collected.Contains(pic) && File.Exists(path) && texts.Items.Any(t=>t.Content.Contains("原备注")),"仅移除图片保留原文件和备注");
var old=Path.Combine(root,"old"); Directory.CreateDirectory(old); File.WriteAllText(Path.Combine(old,"state.json"),"{\"Texts\":[{\"Id\":\"old\",\"Content\":\"旧文字\"}],\"Images\":[],\"Collection\":[]}");
var legacy=new StateStore(old); Check(legacy.State.AutoTextHang && !legacy.State.Texts[0].Hanging && !legacy.State.AutoClipboard,"旧记录不意外挂绳或开启自动获取");
gallery.Collect(path,ImageContent.Read(path));pic=gallery.Collected[0];gallery.SetNote(pic,"原文");
var before=texts.Add("待合并"); var originalCount=texts.Items.Count;
using(var locked=new FileStream(Path.Combine(root,"state.json"),FileMode.Open,FileAccess.Read,FileShare.None)) {
bool threw=false; try{gallery.AttachText(pic,before,false,false);}catch(IOException){threw=true;}
Check(threw && texts.Items.Count==originalCount && texts.Items.Contains(before) && gallery.Note(pic)=="原文","保存失败时关联整体回滚");
}
texts.SetHanging(new[]{before},true);
var previous=gallery.Hanging.Select(i=>i.Path).ToArray();var index=Array.IndexOf(previous,"text:"+before.Id);
gallery.SaveCard(new CaptureItem(before),pic,"新备注",null,false);
Check(gallery.Hanging[index]==pic && gallery.Note(pic)=="新备注" && !texts.Items.Contains(before),"给已有文字配图保持来源卡片位置");
texts.AutoHang=true;texts.Add("混排倒数第二条");var tail=texts.Add("混排最后一条");
Check(gallery.Hanging.Last().Text==tail,"新文字按进入顺序加入混排末尾");
var ordered=gallery.Hanging.Select(i=>i.Path).ToArray();
Check(!gallery.Reorder(tail==null ? "" : "text:"+tail.Id,0,ordered,false) && gallery.Hanging.Select(i=>i.Path).SequenceEqual(ordered),"绳外放下不改序");
Check(!gallery.Reorder("text:missing",0,ordered,true),"过期来源不可排序");
Check(gallery.Reorder("text:"+tail.Id,1,ordered,true) && gallery.Hanging[1].Text==tail,"文字可插入图文混排并保持备注");
Check(new StateStore(root).State.CardOrder.SequenceEqual(store.State.CardOrder),"混排顺序立即持久化");
ordered=gallery.Hanging.Select(i=>i.Path).ToArray();
using(var locked=new FileStream(Path.Combine(root,"state.json"),FileMode.Open,FileAccess.Read,FileShare.None)) {
bool threw=false;try{gallery.Reorder("text:"+tail.Id,0,ordered,true);}catch(IOException){threw=true;}
Check(threw && gallery.Hanging.Select(i=>i.Path).SequenceEqual(ordered),"排序保存失败整体回滚");
}
Console.WriteLine("卡片测试全部通过");
}
}}
