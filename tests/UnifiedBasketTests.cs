using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ScreenLine.Tests {
internal static class UnifiedBasketTests {
static int passed;static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("通过："+name);}
[STAThread]public static void Main(){var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};LightTheme.Install(app);
app.Startup+=async delegate {DesktopController c=null;try{
var root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","unified-"+Guid.NewGuid().ToString("N"),"data");var store=new StateStore(root);
c=new DesktopController(store,Path.Combine(root,"missing"),false);await c.Start();
((System.Windows.Threading.DispatcherTimer)typeof(ShelfWindow).GetField("pointer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(c.Shelf)).Stop();
c.Shelf.Reveal(true,true,0);
var text=c.Texts.Create("会议文字\n保留缩进");
var bitmap=BitmapSource.Create(80,60,96,96,PixelFormats.Bgra32,null,new byte[80*60*4],80*4);
string p1=Path.Combine(root,"picture.png"),p2=Path.Combine(root,"combined.png");File.WriteAllBytes(p1,ImageContent.EncodeClipboard(bitmap));File.WriteAllBytes(p2,ImageContent.EncodeClipboard(bitmap));
var picture=c.Gallery.ImportCard(p1,ImageContent.Read(p1),"",false);var pair=c.Gallery.ImportCard(p2,ImageContent.Read(p2),"图文备注",true);
c.ShowBasket();await Task.Delay(150);var basket=c.Basket;
Check(basket.VisibleCount==3&&c.TextWindow==null&&!c.Shelf.TextButton.IsVisible,"三种内容进入同一个篮子且信封入口隐藏");
Check(Find(basket,"新图片自动上绳")&&Find(basket,"新文字自动上绳"),"图片与文字自动上绳横向独立显示");
var imageBounds=basket.AutoHang.TransformToAncestor(basket).Transform(new Point());var textBounds=basket.TextAutoHang.TransformToAncestor(basket).Transform(new Point());Check(Math.Abs(textBounds.Y-imageBounds.Y)<1&&textBounds.X>imageBounds.X,"两个自动上绳开关位于同一行");
basket.TypeButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(basket.ActiveType==2&&basket.VisibleCount==1,"点击文字按钮筛选");
basket.TypeButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(basket.ActiveType==0&&basket.VisibleCount==3,"再次点击当前类型取消筛选并恢复全部");
basket.ChoiceFor(p1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(basket.SelectedCount==1,"整卡点击选中并显示预览");
basket.ChoiceFor(p2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(basket.SelectedCount==2,"不同缩略图可连续多选");
basket.ChoiceFor(p2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));basket.ChoiceFor(p1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(basket.SelectedCount==0,"再次点击已选卡片取消选择");
basket.UpdateLayout();
var origins=new[]{p1,p2,"text:"+text.Id}.Select(key=>basket.ChoiceFor(key).TransformToAncestor(basket).Transform(new Point())).ToArray();
Check(origins.Max(point=>point.Y)-origins.Min(point=>point.Y)<1&&origins.Select(point=>point.X).Distinct().Count()==3&&basket.ChoiceFor(p1).ActualHeight<=150,"左侧恢复三列紧凑缩略图而非大行");
var actions=new[]{basket.NewMemoButton,basket.HangButton,basket.AllTab,basket.PendingTab}.Concat(basket.TypeButtons).ToArray();
Check(actions.Select(button=>button.TransformToAncestor(basket).Transform(new Point()).Y).Distinct().Count()==1,"操作及类型筛选按钮都在同一行");
var originalWidth=basket.Width;basket.Width=720;basket.UpdateLayout();basket.UpdateLayout();
var lastType=basket.TypeButtons.Last().TransformToAncestor(basket).TransformBounds(new Rect(basket.TypeButtons.Last().RenderSize));var searchBounds=basket.Search.TransformToAncestor(basket).TransformBounds(new Rect(basket.Search.RenderSize));
Check(lastType.Right<searchBounds.Left&&searchBounds.Right<=basket.ActualWidth-16,"窄窗口下单行按钮与搜索框不重叠且保持界面内");basket.Width=originalWidth;basket.UpdateLayout();
basket.ChoiceFor(p1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Filter(basket,2);Check(basket.SelectedCount==0&&!basket.DeleteButton.IsEnabled,"切换筛选清除隐藏条目的勾选，防止误删除");
Filter(basket,1);Check(basket.VisibleCount==1,"图片筛选排除图文与文字");Filter(basket,2);Check(basket.VisibleCount==1&&basket.MemoEditor.Text==text.Content,"文字筛选显示完整可编辑内容");
Filter(basket,3);Check(basket.VisibleCount==1&&basket.MemoEditor.Text=="图文备注","图文筛选同时显示图片与备注");
basket.MemoEditor.Text="更新后的图文\n第二行";await Task.Delay(500);Check(c.Gallery.Note(pair)==basket.MemoEditor.Text&&new StateStore(root).State.ImageNotes[p2]==basket.MemoEditor.Text,"图文备注自动保存并持久化");
Filter(basket,0);basket.Search.Text="会议";Check(basket.VisibleCount==1&&basket.MemoEditor.Text==text.Content,"搜索覆盖统一列表并切换详情");basket.Search.Text="combined";Check(basket.VisibleCount==1,"图片文件名可参与搜索");basket.Search.Text="";
basket.PendingTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(basket.VisibleCount==1,"待挂筛选统一判断三类挂绳状态");basket.AllTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
basket.TextAutoHang.IsChecked=false;Check(!c.Texts.AutoHang&&c.Captures.AutoHang,"关闭文字自动上绳不改变图片设置");basket.AutoHang.IsChecked=false;Check(!c.Captures.AutoHang&&!c.Texts.AutoHang,"图片开关独立保存");basket.AutoHang.IsChecked=true;
basket.ChoiceFor("text:"+text.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Directory.CreateDirectory(Path.Combine(root,"state.json.tmp"));basket.MemoEditor.Text="保存失败草稿";Check(!basket.SaveMemo()&&text.Content!="保存失败草稿","保存失败保留编辑区草稿不改原记录");
basket.ChoiceFor(p2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(basket.MemoEditor.Text=="保存失败草稿","保存失败禁止切换当前备忘");basket.Close();Check(c.Basket==basket&&basket.IsVisible,"保存失败阻止关闭窗口");
Directory.Delete(Path.Combine(root,"state.json.tmp"));Check(basket.SaveMemo()&&text.Content=="保存失败草稿","恢复后可重试保存草稿");await Task.Delay(50);
int before=c.Texts.Items.Count;basket.NewMemoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));basket.MemoEditor.Text="  ";basket.SaveMemo();Check(c.Texts.Items.Count==before,"新建空白草稿不生成记录");
basket.MemoEditor.Text="直接新建的备忘";basket.SaveMemo();await Task.Delay(50);var added=c.Texts.Items.First(t=>t.Content=="直接新建的备忘");Check(!added.Hanging&&basket.VisibleCount==4,"直接新建文字遵循文字自动上绳设置");
basket.ArchiveOption.Toggle.IsChecked=true;Check(store.ShouldArchive("text:"+added.Id)&&!store.State.ArchiveDeleted,"归档开关只影响当前统一详情条目");
basket.SelectAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(basket.SelectedCount==4,"全选覆盖三种类型");basket.HangButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(c.Gallery.Hanging.Count==4&&basket.SelectedCount==0,"批量挂绳同时处理图片文字图文");
basket.UpdateLayout();Render((FrameworkElement)basket.Content,Path.Combine(Path.GetDirectoryName(root),"unified-basket.png"));
basket.SelectAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));basket.DeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(c.Gallery.Collected.Count==0&&c.Texts.Items.Count==0&&basket.VisibleCount==0,"批量删除处理三种类型且不会复活待保存记录");
Console.WriteLine("统一篮子测试："+passed+" 通过，0 失败");
}catch(Exception error){Console.WriteLine(error);Environment.ExitCode=1;}finally{if(c!=null)c.Dispose();app.Shutdown(Environment.ExitCode);}};app.Run();}
static void Filter(BasketWindow basket,int type){if(type==0)basket.AllTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else if(basket.ActiveType!=type)basket.TypeButtons[type-1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
static bool Find(DependencyObject node,string text){var block=node as TextBlock;if(block!=null&&block.Text==text)return true;for(int n=0;n<VisualTreeHelper.GetChildrenCount(node);n++)if(Find(VisualTreeHelper.GetChild(node,n),text))return true;return false;}
static void Render(FrameworkElement element,string path){element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);Console.WriteLine("预览："+path);}
}}
