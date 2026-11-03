using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
namespace ScreenLine.Tests {
internal static class CardUiTests {
[STAThread] public static void Main() {
new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
var text=new TextItem {Id="ui",Content="这是完整的文字\n第二行",Hanging=true};
var card=new ShotCard(new CaptureItem(text),delegate{});
if(card.Surface.Child==null || card.Grip.IsEnabled) throw new Exception("文字卡片应可见，且不可调用图片贴图");
if(!FindText(card.Surface,text.Content)) throw new Exception("文字必须以真实文字显示");
if(AutomationProperties.GetName(card.ActionMenu.Actions[ShotAction.Remove])!="取下放篮子" || AutomationProperties.GetName(card.ActionMenu.Actions[ShotAction.Trash])!="删除文字")throw new Exception("文字菜单不能误称篮子或回收站");
int chosen=-1; card.ChooseBackground=delegate(int color){chosen=color;card.SetBackground(color);};
var menuSurface=(FrameworkElement)card.ActionMenu.Items[0];
menuSurface.Measure(new Size(220,1000)); menuSurface.Arrange(new Rect(0,0,220,menuSurface.DesiredSize.Height)); menuSurface.UpdateLayout();
for(int n=0;n<ImageMenu.CardPalette.Length;n++) {
var option=card.ActionMenu.ColorButtons[n];
if((string)option.Content!=ImageMenu.CardColorNames[n])throw new Exception("颜色应显示简短中文名称");
var bounds=option.TransformToAncestor(menuSurface).TransformBounds(new Rect(option.RenderSize));
if(bounds.Left<0 || bounds.Right>menuSurface.ActualWidth || bounds.Top<0 || bounds.Bottom>menuSurface.ActualHeight)throw new Exception("六个颜色选项必须完整位于菜单内");
card.ActionMenu.ColorButtons[n].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
if(chosen!=n || ((SolidColorBrush)card.Surface.Background).Color!=(Color)ColorConverter.ConvertFromString(ImageMenu.CardPalette[n]))throw new Exception("右键选色应更新对应卡片背景");
}
card.Dispose();
var root=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","preview-"+Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(root);var path=System.IO.Path.Combine(root,"original.png");
var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[16],8);
System.IO.File.WriteAllBytes(path,ImageContent.EncodeClipboard(bitmap));var gallery=new Gallery(new StateStore(root));var picture=gallery.ImportCard(path,ImageContent.Read(path),"完整备注",true);
string opened=null;var viewer=new CardEditorWindow(gallery,picture,true,delegate(string value){opened=value;});viewer.OpenPicturePreview();
if(opened!=path||!viewer.Body.IsReadOnly||viewer.Body.Text!="完整备注"||viewer.Topmost)throw new Exception("图文详情应打开原图片且保留备注，不遮挡图片查看器");
opened=null;var textViewer=new CardEditorWindow(gallery,new CaptureItem(text),true,delegate(string value){opened=value;});textViewer.OpenPicturePreview();
if(opened!=null)throw new Exception("纯文字详情不能打开图片查看器");
System.IO.File.Delete(path);opened=null;viewer.OpenPicturePreview();if(opened!=null)throw new Exception("原图片缺失时不能调用查看器");
viewer.Close();textViewer.Close();Console.WriteLine("卡片界面测试：24 通过，0 失败");
Application.Current.Shutdown();
}
static bool FindText(DependencyObject node,string value) {var text=node as TextBlock; if(text!=null && text.Text==value)return true; for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(FindText(VisualTreeHelper.GetChild(node,i),value))return true; return false;}
}}
