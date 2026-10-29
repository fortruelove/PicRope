using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLine.Tests
{
    internal static class EnvelopeDetailTests
    {
        static int passed;
        static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("通过：" + name); }
        [STAThread] public static void Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LightTheme.Install(app);
            app.Startup += async delegate
            {
                ShelfWindow shelf = null; TextWindow window = null;
                try
                {
                    var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "envelope-" + Guid.NewGuid().ToString("N"));
                    var store = new StateStore(root); var texts = new TextCollection(store); var gallery = new Gallery(store);
                    var first = texts.Add("会议纪要\n  需要完整保留的第二行"); var second = texts.Add("拍摄清单\n1. 检查电量\n2. 准备素材");
                    int notices = 0; texts.Changed += delegate { notices++; };
                    texts.UpdateContent(first, "新纪要\n\t缩进保留");
                    Check(new StateStore(root).State.Texts.Single(t => t.Id == first.Id).Content == first.Content && notices == 1, "编辑持久化原文并通知更新");
                    string previous = first.Content;
                    Directory.CreateDirectory(Path.Combine(root, "state.json.tmp"));
                    bool failed = false; try { texts.UpdateContent(first, "不应保存"); } catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
                    Check(failed && first.Content == previous && notices == 1, "保存失败回滚原文且不发成功通知");
                    Directory.Delete(Path.Combine(root, "state.json.tmp"));
                    shelf = new ShelfWindow(gallery, delegate { }, delegate { }); shelf.Show();
                    ((System.Windows.Threading.DispatcherTimer)typeof(ShelfWindow).GetField("pointer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(shelf)).Stop();
                    shelf.Reveal(true, true, 60);
                    int pasteCalls = 0;
                    window = new TextWindow(texts, shelf, delegate { pasteCalls++; return Task.FromResult("已粘贴"); }, delegate { });
                    window.Show(); await Task.Delay(150); window.UpdateLayout();
                    window.List.SelectedItem = first; window.UpdateLayout();
                    Check(window.DetailEditor.Text == first.Content && !window.DetailEditor.IsReadOnly, "右侧显示所选条目全文且可编辑");
                    var listBounds = window.List.TransformToAncestor(window).TransformBounds(new Rect(window.List.RenderSize));
                    var detailBounds = window.DetailEditor.TransformToAncestor(window).TransformBounds(new Rect(window.DetailEditor.RenderSize));
                    Check(detailBounds.Left > listBounds.Right, "列表在左，详情编辑区在右");
                    var row = (ListBoxItem)window.List.ItemContainerGenerator.ContainerFromItem(first);
                    double rowHeight = row.ActualHeight;
                    window.DetailEditor.Text = "自动保存的纪要\n  第二行\n\t第三行";
                    await Task.Delay(650);
                    Check(new StateStore(root).State.Texts.Single(t => t.Id == first.Id).Content == window.DetailEditor.Text, "短暂停顿后自动保存");
                    window.UpdateLayout();
                    Check(Math.Abs(row.ActualHeight - rowHeight) < 1 && !Children<TextBox>(row).Any(), "左侧条目高度固定，选中不在行内展开");
                    window.DetailEditor.Text = "新记录到达时也要保存的编辑"; window.DetailEditor.CaretIndex = 3;
                    texts.Add("稍后处理的临时记录");
                    Check(window.List.SelectedItem == first && window.DetailEditor.CaretIndex == 3 && first.Content == window.DetailEditor.Text, "新增记录时保存草稿并保持当前选择和光标");
                    window.DetailEditor.Text = "筛选前保存的编辑"; window.Search.Text = "不存在的关键词";
                    Check(first.Content == "筛选前保存的编辑" && window.VisibleCount == 0 && !window.DetailEditor.IsEnabled, "筛选切换先保存，空结果清空详情");
                    window.Search.Text = ""; window.List.SelectedItem = first;
                    window.Search.Text = "筛选前保存"; window.List.SelectedItem = first;
                    window.DetailEditor.Text = "已不再匹配查询的编辑"; await Task.Delay(650);
                    Check(window.VisibleCount == 0 && !window.DetailEditor.IsEnabled, "自动保存后重新应用当前搜索条件");
                    window.Search.Text = ""; window.List.SelectedItem = first;
                    window.DetailEditor.Text = "切换前必须保存"; window.List.SelectedItem = second;
                    Check(first.Content == "切换前必须保存" && window.DetailEditor.Text == second.Content, "切换条目先保存再载入详情");
                    window.DetailEditor.Text = "保存失败时保留的草稿";
                    Directory.CreateDirectory(Path.Combine(root, "state.json.tmp"));
                    window.List.SelectedItem = first;
                    Check(window.List.SelectedItem == second && window.DetailEditor.Text == "保存失败时保留的草稿", "保存失败阻止切换并保留草稿");
                    window.Close(); Check(window.IsVisible, "保存失败阻止关闭丢失草稿");
                    Directory.Delete(Path.Combine(root, "state.json.tmp"));
                    window.List.SelectedItem = first;
                    Check(second.Content == "保存失败时保留的草稿", "恢复后切换重试保存成功");
                    var buttons = Children<Button>(window).Where(b => b.Content is string && new[] { "新建文字", "粘贴文字", "挂上绳子", "配图 / 编辑…", "全选", "删除所选" }.Contains((string)b.Content)).ToArray();
                    var actions = buttons.Where(b => new[] { "新建文字", "粘贴文字", "挂上绳子", "配图 / 编辑…" }.Contains((string)b.Content)).ToArray();
                    Check(actions.Length == 4 && actions.Max(b => b.TransformToAncestor(window).Transform(new Point()).Y) - actions.Min(b => b.TransformToAncestor(window).Transform(new Point()).Y) < 2 && actions.Max(b => b.TransformToAncestor(window).Transform(new Point(b.ActualWidth,0)).X) < window.Search.TransformToAncestor(window).Transform(new Point()).X, "新建等四个操作按钮在搜索框左侧并保持同一行");
                    Check(window.SelectAllButton.ActualWidth == 112 && window.SelectAllButton.ActualHeight == 38 && ((SolidColorBrush)window.SelectAllButton.Background).Color == Color.FromRgb(228,238,252) && window.DeleteSelectedButton.ActualWidth == 112 && window.DeleteSelectedButton.ActualHeight == 38 && window.DeleteSelectedButton.Tag == null, "底部全选与删除使用篮子同款尺寸和样式");
                    var toggle = Children<CheckBox>(window).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "新文字自动上绳");
                    Check(toggle.Template == LightTheme.AutoHangSwitchTemplate && toggle.Content == null, "文字自动上绳使用篮子同款开关");
                    window.Width = 680; window.UpdateLayout();
                    Check(buttons.All(b => b.TransformToAncestor(window).Transform(new Point(b.ActualWidth, 0)).X < window.Width), "较窄窗口按钮仍在可见范围内");
                    window.Width = 960;
                    window.DetailEditor.Text = "关闭前保存的内容"; window.Close(); window = null;
                    Check(new StateStore(root).State.Texts.Single(t => t.Id == first.Id).Content == "关闭前保存的内容", "关闭信封前完成保存");
                    window = new TextWindow(texts, shelf, delegate { return Task.FromResult(""); }, delegate { }); window.Show(); window.UpdateLayout();
                    var disposable = texts.Add("需要删除的临时文字"); window.List.SelectedItem = disposable;
                    window.DetailEditor.Text = "删除时尚未保存的修改"; texts.Remove(disposable); await Task.Delay(500);
                    Check(!texts.Items.Contains(disposable) && !new StateStore(root).State.Texts.Any(t => t.Id == disposable.Id), "删除当前条目不会被待保存草稿复活");
                    texts.UpdateContent(second, "拍摄清单\n准备素材、检查电量与确认配音");
                    int beforeNew = texts.Items.Count; string existing = first.Content;
                    window.NewTextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(window.DetailEditor.IsEnabled && window.DetailEditor.Text == "" && window.List.SelectedItem == null && texts.Items.Count == beforeNew, "新建复用右侧详情框且不产生空记录");
                    window.DetailEditor.Text = "  \n\t"; await Task.Delay(650);
                    Check(texts.Items.Count == beforeNew, "空白草稿不自动创建记录");
                    window.DetailEditor.Text = "直接输入的新文字\n  保留缩进"; await Task.Delay(650);
                    var created = texts.Items.First();
                    Check(texts.Items.Count == beforeNew + 1 && created.Content == window.DetailEditor.Text && first.Content == existing && window.List.SelectedItem == created, "直接输入自动创建并选中新记录，不覆盖已有文字");
                    window.DetailEditor.Text += "\n继续编辑"; await Task.Delay(650);
                    Check(texts.Items.Count == beforeNew + 1 && texts.Items.First().Id == created.Id, "新记录后续编辑不重复创建");
                    window.NewTextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.DetailEditor.Text = existing; await Task.Delay(650);
                    Check(texts.Items.Count == beforeNew + 2 && texts.Items.First().Id != first.Id, "主动新建允许同文记录，不合并旧条目");
                    texts.AutoHang = false;
                    window.NewTextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.DetailEditor.Text = "只留在信封的新文字"; await Task.Delay(650);
                    Check(!texts.Items.First().Hanging && new StateStore(root).State.Texts.First().Content == "只留在信封的新文字", "新建遵循文字自动上绳设置并持久保存");
                    window.NewTextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); int beforeFailure = texts.Items.Count;
                    Directory.CreateDirectory(Path.Combine(root, "state.json.tmp")); window.DetailEditor.Text = "新建保存失败仍保留的内容"; await Task.Delay(650);
                    Check(texts.Items.Count == beforeFailure && window.DetailEditor.Text == "新建保存失败仍保留的内容", "新建保存失败保留草稿，不留下空记录");
                    window.Close(); Check(window.IsVisible, "新建保存失败阻止关闭"); Directory.Delete(Path.Combine(root, "state.json.tmp"));
                    window.List.SelectedItem = first;
                    Check(texts.Items.Count == beforeFailure + 1 && texts.Items.First().Content == "新建保存失败仍保留的内容", "恢复后切换重试创建一次");
                    window.List.SelectedItem = first;
                    window.DetailEditor.Text = "会议纪要\n\n项目进展\n  • 完成素材整理\n  • 下周开始剪辑\n\n下一步\n  1. 确认配音\n  2. 补充参考图片"; await Task.Delay(650);
                    Render(window, Path.Combine(root, "envelope-details.png"));
                    Console.WriteLine("预览：" + Path.Combine(root, "envelope-details.png"));
                    Console.WriteLine("信封详情测试：{0} 通过，0 失败", passed);
                }
                catch (Exception error) { Console.WriteLine(error); Environment.ExitCode = 1; }
                finally { if (window != null) window.Close(); if (shelf != null) shelf.Close(); app.Shutdown(Environment.ExitCode); }
            };
            app.Run();
        }
        static System.Collections.Generic.IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T) yield return (T)child; foreach (var match in Children<T>(child)) yield return match; }
        }
        static void Render(Window window, string path)
        {
            window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32); image.Render(window);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using (var stream = File.Create(path)) png.Save(stream);
        }
    }
}
