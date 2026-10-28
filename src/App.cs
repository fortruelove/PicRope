using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("PicRope · 图绳")]
[assembly: AssemblyProduct("PicRope")]
[assembly: AssemblyDescription("图绳：把截图挂在屏幕上沿，随手复制、拖拽和保存。")]
[assembly: AssemblyVersion("1.2.5.0")]
[assembly: AssemblyFileVersion("1.2.5.0")]
[assembly: AssemblyInformationalVersion("1.2.5")]

namespace ScreenLine
{
    internal sealed class DesktopController : IDisposable
    {
        public readonly Gallery Gallery;
        public readonly CapturePipeline Captures;
        public readonly TextCollection Texts;
        public readonly ShelfWindow Shelf;
        readonly WindowsEvents events;
        readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        LightNotice notice;
        readonly CheckBox collect;
        readonly CheckBox collectText;
        readonly CheckBox autoHang;
        internal readonly TrayPanel TrayMenu;
        internal AppearanceWindow Appearance { get; private set; }
        internal BasketWindow Basket { get; private set; }
        internal TextWindow TextWindow { get; private set; }
        internal CaptureWindow Capture { get; private set; }
        internal ShortcutWindow ShortcutSettings { get; private set; }
        readonly MenuItem captureMenu;
        internal readonly List<ImageEditorWindow> Editors = new List<ImageEditorWindow>();
        readonly AppUpdates updates = new AppUpdates();
        readonly CancellationTokenSource updateLifetime = new CancellationTokenSource();
        readonly DispatcherTimer updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        readonly MenuItem updateMenu;
        readonly bool automaticUpdates;
        UpdateRelease availableUpdate;
        UpdateWindow updateWindow;
        bool checkingUpdates, disposed;
        uint textClipboardSequence;
        int textClipboardVersion;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public DesktopController(StateStore store, string screenshots, bool automaticUpdates = true)
        {
            this.automaticUpdates = automaticUpdates;
            Gallery = new Gallery(store);
            Texts = new TextCollection(store);
            Captures = new CapturePipeline(Gallery, Application.Current.Dispatcher, screenshots);
            Shelf = new ShelfWindow(Gallery, Act, EnableClipboard);
            Shelf.SortingFailed += Notice;
            Shelf.BasketRequested += ShowBasket;
            Shelf.TextRequested += ShowTexts;
            Texts.Changed += Shelf.Update;
            Texts.Changed += Shelf.UpdateTexts;
            Gallery.Changed += Texts.Notify;
            events = new WindowsEvents();
            events.RegisterCapture(store.State.CaptureModifiers, store.State.CaptureKey);
            events.CaptureRequested += ShowCapture;
            events.ToggleRequested += Shelf.Toggle;
            events.ExitRequested += Exit;
            // One clipboard snapshot owns both image and text; no independent readers race.
            textClipboardSequence = WindowsNative.GetClipboardSequenceNumber();
            events.ClipboardChanged += CollectClipboardText;
            Captures.Arrived += Shelf.ShowNewCapture;
            Captures.Failed += Notice;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PicRope.Icon"))
                tray.Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            tray.Text = "PicRope · 图绳";
            TrayMenu = new TrayPanel();
            SyncScreens();
            TrayMenu.Opened += delegate { SyncScreens(); };
            TrayMenu.ScreenMenu.Opened += delegate { SyncScreens(); };
            TrayMenu.OnAction(TrayMenu.VisibilityButton, delegate { Shelf.Toggle(); });
            TrayMenu.OnToggle(TrayMenu.PinRope, store.State.KeepRopeVisible, delegate(bool value) {
                try {Shelf.SetKeepRopeVisible(value);}
                catch(Exception error){store.Log(error);TrayMenu.PinRope.IsChecked=store.State.KeepRopeVisible;Notice("无法保存置顶设置，请重试。");}
            });
            if (events.ShortcutAvailable) TrayMenu.Shortcut.Text = "Ctrl+Alt+T";
            else Notice("Ctrl+Alt+T 已被占用，可点击托盘图标打开。");
            TrayMenu.OnAction(TrayMenu.BasketButton, ShowBasket);
            TrayMenu.OnAction(TrayMenu.TextButton, ShowTexts);
            autoHang = TrayMenu.AutoHang;
            TrayMenu.OnToggle(autoHang, Captures.AutoHang, delegate(bool value) { if (Captures.AutoHang != value) Captures.AutoHang = value; });
            Gallery.Changed += SyncAutoHang;
            collect = TrayMenu.Pictures;
            collectText = TrayMenu.Text;
            TrayMenu.OnToggle(collect, store.State.CollectClipboard, delegate(bool value){SetAutomaticClipboard(true,value);});
            TrayMenu.OnToggle(collectText, store.State.CollectTextClipboard, delegate(bool value){SetAutomaticClipboard(false,value);});
            TrayMenu.OnAction(TrayMenu.AppearanceButton, ShowAppearance);
            var more = TrayMenu.MoreMenu;
            MenuItem previewToggle = null;
            previewToggle = more.AddAction("悬停预览", "picture", delegate
            {
                bool previous = store.State.HoverPreview;
                store.State.HoverPreview = previewToggle.IsChecked;
                try { store.Save(); Shelf.Preview.SetEnabled(store.State.HoverPreview); }
                catch (Exception error) { store.State.HoverPreview = previous; previewToggle.IsChecked = previous; store.Log(error); Notice("预览设置保存失败"); }
            });
            previewToggle.IsCheckable = true;
            previewToggle.IsChecked = store.State.HoverPreview;
            captureMenu = more.AddAction("区域截图", "capture", ShowCapture);
            UpdateCaptureLabel();
            more.AddAction("截图快捷键…", "shortcut", delegate
            {
                if (ShortcutSettings != null) { ShortcutSettings.Activate(); return; }
                ShortcutSettings = new ShortcutWindow(store.State.CaptureModifiers, store.State.CaptureKey, SetCaptureShortcut);
                ShortcutSettings.Closed += delegate { ShortcutSettings = null; }; ShortcutSettings.Show();
            });
            more.Separator();
            more.AddAction("添加图片…", "add", async delegate
            {
                var picker = new OpenFileDialog { Title = "添加图片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", Multiselect = true };
                if (picker.ShowDialog() != true) return;
                foreach (string file in picker.FileNames)
                    if (!await Captures.Import(file, null, true)) Notice("无法读取图片：" + Path.GetFileName(file));
            });
            more.AddAction("打开收集文件夹", "folder", delegate
            {
                try { WindowsShell.OpenInbox(store); }
                catch (Exception error) { store.Log(error); Notice("无法打开收集文件夹：" + error.Message); }
            });
            more.AddAction("全部取下（保留文件）", "clear", delegate { Gallery.Clear(); Texts.SetHanging(Texts.Items.ToArray(),false); });
            more.Separator();
            MenuItem startup = null;
            startup = more.AddAction("开机启动", "startup", delegate
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (startup.IsChecked) key.SetValue("PicRope", Quote(Assembly.GetEntryAssembly().Location));
                    else key.DeleteValue("PicRope", false);
                }
            });
            startup.IsCheckable = true;
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) startup.IsChecked = key != null && key.GetValue("PicRope") != null;
            updateMenu = more.AddAction("检查更新…", "save", async delegate { await CheckForUpdates(true); });
            updateMenu.InputGestureText = "v" + AppUpdates.CurrentVersion;
            updateTimer.Tick += async delegate { updateTimer.Interval = TimeSpan.FromHours(12); await CheckForUpdates(false); };
            tray.BalloonTipClicked += delegate { ShowUpdate(); };
            TrayMenu.OnAction(TrayMenu.ExitButton, Exit);
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Right) ShowTrayMenu(); };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Left) Shelf.Toggle(); };
            tray.Visible = true;
        }

        async void Exit()
        {
            foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return;
            Application.Current.Shutdown();
        }

        public async Task Start()
        {
            Shelf.Show();
            Shelf.Reveal(true, false, 4);
            await Captures.Start();
            if (automaticUpdates) updateTimer.Start();
        }

        void UpdateCaptureLabel()
        {
            captureMenu.InputGestureText = events.CaptureShortcutAvailable ? ShortcutWindow.Label(Gallery.Store.State.CaptureModifiers, Gallery.Store.State.CaptureKey) : "快捷键被占用";
        }

        internal bool SetCaptureShortcut(uint modifiers, uint key)
        {
            if (!events.RegisterCapture(modifiers, key)) return false;
            uint previousModifiers = Gallery.Store.State.CaptureModifiers, previousKey = Gallery.Store.State.CaptureKey;
            Gallery.Store.State.CaptureModifiers = modifiers; Gallery.Store.State.CaptureKey = key;
            try { Gallery.Store.Save(); }
            catch (Exception error)
            {
                Gallery.Store.State.CaptureModifiers = previousModifiers; Gallery.Store.State.CaptureKey = previousKey;
                events.RegisterCapture(previousModifiers, previousKey); Gallery.Store.Log(error); Notice("无法保存快捷键，请重试。"); return false;
            }
            UpdateCaptureLabel(); return true;
        }

        internal async void ShowCapture()
        {
            if (disposed || Shelf.Capturing) return;
            Shelf.Capturing = true;
            var display = WindowsNative.ScreenAtPointer();
            bool revealed = Shelf.Revealed;
            try
            {
                TrayMenu.Close(); if (Basket != null) Basket.Close(); if (TextWindow != null) TextWindow.Close();
                if (Appearance != null) Appearance.Close(); if (ShortcutSettings != null) ShortcutSettings.Close();
                Shelf.Reveal(false, false, 0); await Task.Delay(260);
                if (disposed) return;
                Capture = new CaptureWindow(display);
                var result = await Capture.PickAsync(); Capture = null;
                if (disposed) return;
                if (result != null) await Captures.AcceptCapture(result);
                else if (revealed) Shelf.Reveal(true, false, 2);
            }
            catch (Exception error) { if (!disposed) { Gallery.Store.Log(error); Notice("截图未完成，请重试。"); } }
            finally { if (Capture != null) Capture.Close(); Capture = null; Shelf.Capturing = false; if(Gallery.Store.State.KeepRopeVisible)Shelf.Reveal(true,false,0); }
        }

        async Task CheckForUpdates(bool manual)
        {
            if (disposed || checkingUpdates) return;
            if (manual && availableUpdate != null) { ShowUpdate(); return; }
            checkingUpdates = true; updateMenu.IsEnabled = false; updateMenu.Header = "正在检查更新…";
            try
            {
                availableUpdate = await updates.CheckAsync(updateLifetime.Token);
                if (disposed) return;
                if (availableUpdate == null) { if (manual) Notice("当前已是最新版 " + AppUpdates.CurrentVersion + "。"); }
                else if (manual) ShowUpdate();
                else if (Gallery.Store.State.LastUpdateNotification != availableUpdate.Version)
                {
                    tray.ShowBalloonTip(10000, "图绳有新版本 " + availableUpdate.Version, "点击查看更新并下载安装。", Forms.ToolTipIcon.Info);
                    Gallery.Store.State.LastUpdateNotification = availableUpdate.Version; Gallery.Store.Save();
                }
            }
            catch (Exception error)
            {
                if (!disposed && manual) Notice("暂时无法检查更新，请检查网络后重试。" + (error is System.IO.InvalidDataException ? "\n" + error.Message : ""));
            }
            finally
            {
                checkingUpdates = false; updateMenu.IsEnabled = true;
                updateMenu.Header = availableUpdate == null ? "检查更新…" : "更新到 " + availableUpdate.Version + "…";
            }
        }

        void ShowUpdate()
        {
            if (disposed || availableUpdate == null) return;
            if (updateWindow != null) { updateWindow.Activate(); return; }
            updateWindow = new UpdateWindow(availableUpdate, updates, async delegate(string work)
            {
                foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return false;
                AppUpdates.Install(work);
                Application.Current.Shutdown();
                return true;
            });
            updateWindow.Closed += delegate { updateWindow = null; };
            updateWindow.Show();
        }

        void EnableClipboard()
        {
            collect.IsChecked = true;
        }

        void SyncAutoHang() { autoHang.IsChecked = Captures.AutoHang; }

        void SyncScreens()
        {
            TrayMenu.SetScreens(WindowsNative.Screens, Gallery.Store.State.DisplayDevice, SelectScreen);
        }

        void SelectScreen(string device)
        {
            TrayMenu.Close();
            try { Shelf.SetDisplay(device); }
            catch (Exception error) { Gallery.Store.Log(error); Notice("无法保存屏幕设置：" + error.Message); }
            SyncScreens();
        }

        void SetAutomaticClipboard(bool picture, bool value)
        {
            var state=Gallery.Store.State;
            bool old=state.AutoClipboard, pictures=state.CollectClipboard, text=state.CollectTextClipboard;
            if(picture)state.CollectClipboard=value;else state.CollectTextClipboard=value;
            state.AutoClipboard=state.CollectClipboard || state.CollectTextClipboard;
            try { Gallery.Store.Save(); }
            catch { state.AutoClipboard=old; state.CollectClipboard=pictures; state.CollectTextClipboard=text; throw; }
            textClipboardVersion++; textClipboardSequence=WindowsNative.GetClipboardSequenceNumber();
            Shelf.Update();
        }
        async void CollectClipboardText()
        {
            uint sequence=WindowsNative.GetClipboardSequenceNumber();
            if(disposed || !(Gallery.Store.State.CollectClipboard || Gallery.Store.State.CollectTextClipboard) || sequence==textClipboardSequence) return;
            textClipboardSequence=sequence; int version=textClipboardVersion;
            try {
                var card=await CardClipboard.Read(sequence,false,Gallery.Store.State.CollectClipboard,Gallery.Store.State.CollectTextClipboard);
                if(disposed || version!=textClipboardVersion || sequence!=WindowsNative.GetClipboardSequenceNumber()) return;
                card=CardClipboard.ForAutomatic(card,Gallery.Store.State.CollectClipboard,Gallery.Store.State.CollectTextClipboard);
                await AcceptCard(card, delegate { return !disposed && version == textClipboardVersion && sequence == WindowsNative.GetClipboardSequenceNumber(); });
            } catch(Exception error) {if(!disposed)Gallery.Store.Log(error);}
        }
        internal async Task AcceptCard(ClipboardCard card, Func<bool> stillCurrent = null)
        {
            if(card==null || disposed || (stillCurrent!=null && !stillCurrent()))return;
            if(card.Picture!=null) await Captures.ImportClipboardCard(card, stillCurrent);
            else {
                var text=Texts.Add(card.Text);
                if(text!=null && Texts.AutoHang) Texts.SetHanging(new[]{text},true);
            }
            if(!disposed) Shelf.Update();
        }
        internal async Task<string> PasteCard(bool pictureRequired)
        {
            try {
                var card=await CardClipboard.Read(WindowsNative.GetClipboardSequenceNumber(),true);
                if(card==null || (pictureRequired && card.Picture==null))return pictureRequired ? "剪贴板中没有图片" : "剪贴板中没有文字或图片";
                await AcceptCard(card);
                return card.Picture!=null ? "已粘贴到图片篮子" : "已粘贴文字";
            } catch(Exception error){Gallery.Store.Log(error); return "粘贴未完成，请重试";}
        }
        internal async void EditCard(CaptureItem item, bool readOnly)
        {
            if (Basket!=null) Basket.Close();
            if (TextWindow!=null) TextWindow.Close();
            Shelf.ChoosingTexts=true;
            try {
                var dialog=new CardEditorWindow(Gallery,item,readOnly) {Owner=Shelf};
                if(dialog.ShowDialog()!=true || readOnly)return;
                CaptureItem picture=dialog.SelectedPicture;
                string newPath=null;
                if(dialog.NewPicture!=null) {
                    string path=Path.Combine(Gallery.Store.Inbox,"配图_"+Guid.NewGuid().ToString("N")+dialog.NewPicture.Extension);
                    Directory.CreateDirectory(Gallery.Store.Inbox);
                    await Task.Run(()=>File.WriteAllBytes(path,dialog.NewPicture.Bytes));
                    newPath=path;
                    picture=new CaptureItem(path,ImageContent.Decode(dialog.NewPicture.Bytes));
                }
                string content=dialog.Body.Text;
                bool append=dialog.SelectedText!=null && dialog.Append;
                if(picture!=null && append && !item.IsText && !string.IsNullOrEmpty(Gallery.Note(item))) {
                    content=Gallery.Note(item)+"\n"+content;
                    append=false;
                }
                if(picture!=null && !string.IsNullOrEmpty(Gallery.Note(picture))) {
                    var choice=MessageBox.Show(dialog.Owner,"所选图片已有备注。是：追加文字；否：替换备注；取消：放弃合并。","合并内容卡片",MessageBoxButton.YesNoCancel);
                    if(choice==MessageBoxResult.Cancel)return;
                    append=choice==MessageBoxResult.Yes;
                }
                try { Gallery.SaveCard(item,picture,content,dialog.SelectedText,append,newPath!=null); }
                catch { if(newPath!=null)File.Delete(newPath); throw; }
                Texts.Notify(); Shelf.Update();
            } catch(Exception error){Gallery.Store.Log(error);Notice("保存未完成："+error.Message);}
            finally {Shelf.ChoosingTexts=false;}
        }

        internal void ShowBasket()
        {
            if (Basket != null) { Basket.Activate(); return; }
            Gallery.Prune();
            Basket = new BasketWindow(Gallery, delegate(bool value) { Captures.AutoHang = value; }, Captures.Hang, Act, delegate { return PasteCard(true); });
            Basket.Owner = Shelf;
            Shelf.ChoosingBasket = true;
            Shelf.Reveal(true, false, 0);
            Basket.Closed += delegate { Basket = null; Shelf.ChoosingBasket = false; };
            Basket.Show();
        }

        internal void ShowTexts()
        {
            if (TextWindow != null) { TextWindow.Close(); return; }
            Shelf.Reveal(true, false, 0);
            Shelf.ChoosingTexts = true;
            TextWindow = new TextWindow(Texts, Shelf, delegate { return PasteCard(false); }, delegate(TextItem item) { EditCard(new CaptureItem(item), false); });
            TextWindow.Closed += delegate { TextWindow = null; Shelf.ChoosingTexts = false; };
            TextWindow.Show();
        }

        void ShowAppearance()
        {
            if (Appearance != null) { Appearance.Activate(); return; }
            Appearance = new AppearanceWindow(Shelf.Appearance, Gallery, delegate(LineTheme theme)
            {
                Gallery.Store.State.Theme = theme.Id;
                Gallery.Store.Save();
                Shelf.ApplyTheme(theme);
                Shelf.Reveal(true, false, 3);
            });
            Appearance.Owner = Shelf;
            Shelf.ChoosingAppearance = true;
            Shelf.Reveal(true, false, 0);
            Appearance.Closed += delegate { Appearance = null; Shelf.ChoosingAppearance = false; };
            Appearance.Show();
        }

        internal void ShowTrayMenu()
        {
            WindowsNative.SetForegroundWindow(Shelf.Handle);
            var cursor = WindowsNative.Cursor();
            var point = Shelf.PointFromScreen(new Point(cursor.X, cursor.Y));
            TrayMenu.PlacementTarget = Shelf;
            TrayMenu.Placement = PlacementMode.RelativePoint;
            TrayMenu.HorizontalOffset = point.X - 16;
            TrayMenu.VerticalOffset = point.Y - 16;
            TrayMenu.IsOpen = true;
        }

        void Notice(string message)
        {
            if (notice != null) notice.Close();
            notice = new LightNotice(message);
            var current = notice;
            current.Closed += delegate { if (notice == current) notice = null; };
            notice.Show();
        }

        static string Quote(string path) { return "\"" + path + "\""; }

        public async void Act(ShotAction action, CaptureItem item)
        {
            try
            {
                if (action==ShotAction.CardEdit || (item.IsText && action==ShotAction.Edit)) {EditCard(item,false);return;}
                if (action==ShotAction.Open && (item.IsText || !string.IsNullOrEmpty(Gallery.Note(item)))) {EditCard(item,true);return;}
                if (action==ShotAction.CopyText || (item.IsText && action==ShotAction.Copy)) {
                    string value=item.IsText ? item.Text.Content : Gallery.Note(item);
                    var data=new DataObject();data.SetText(value);data.SetData(ClipboardRules.OwnFormat,"1");
                    await WindowsClipboard.Write(data); if(item.IsText ? item.Text.Hanging : Gallery.Contains(item.Path)) Shelf.ShowCopied(item.Path); return;
                }
                if(item.IsText) {
                    if(action==ShotAction.Remove)Texts.SetHanging(new[]{item.Text},false);
                    else if(action==ShotAction.Trash)Texts.Remove(item.Text);
                    return;
                }
                if(action==ShotAction.Split){Gallery.Split(item,Texts);return;}
                if(action==ShotAction.RemoveNote){Gallery.SetNote(item,"");return;}
                if(action==ShotAction.RemovePicture){Gallery.RemovePicture(item,Texts);return;}
                if (!File.Exists(item.Path)) { Gallery.Prune(); return; }
                switch (action)
                {
                    case ShotAction.Copy:
                    case ShotAction.CopyBoth:
                        var payload = new DataObject();
                        byte[] png;
                        using (var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                            payload.SetImage(image);
                            png = ImageContent.EncodeClipboard(image);
                        }
                        payload.SetData("PNG", new MemoryStream(png));
                        payload.SetFileDropList(new StringCollection { item.Path });
                        payload.SetData(ClipboardRules.OwnFormat, "1");
                        if(action==ShotAction.CopyBoth && !string.IsNullOrEmpty(Gallery.Note(item))) payload.SetText(Gallery.Note(item));
                        await WindowsClipboard.Write(payload);
                        Shelf.ShowCopied(item.Path);
                        break;
                    case ShotAction.Open:
                        Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                        Shelf.FollowImageViewer(item.Path);
                        break;
                    case ShotAction.Edit:
                        var existing = Editors.Find(editor => FileRules.SameFile(editor.ImagePath, item.Path));
                        if (existing != null)
                        {
                            existing.Present();
                            break;
                        }
                        var window = new ImageEditorWindow(item.Path, Gallery.Store, RefreshEditedImage, Captures.IgnoreCurrentClipboard);
                        Editors.Add(window);
                        window.Closed += delegate
                        {
                            Editors.Remove(window);
                            if (!window.ReturnBounds.IsEmpty) Shelf.ShowEditedImage(window.ImagePath, window.ReturnBounds, window.ReturnPicture);
                        };
                        Shelf.Reveal(false, false, 0);
                        window.Present();
                        break;
                    case ShotAction.Save:
                        var dialog = new SaveFileDialog { Title = "保存副本", FileName = Path.GetFileName(item.Path), Filter = "图片|*" + Path.GetExtension(item.Path), OverwritePrompt = true };
                        if (dialog.ShowDialog() == true && !FileRules.SameFile(item.Path, dialog.FileName)) File.Copy(item.Path, dialog.FileName, true);
                        break;
                    case ShotAction.Locate:
                        Process.Start("explorer.exe", "/select," + Quote(item.Path));
                        break;
                    case ShotAction.Remove:
                        Shelf.ShowCollect(item);
                        Gallery.Remove(item);
                        break;
                    case ShotAction.Trash:
                        FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        Shelf.ShowDiscard(item);
                        Gallery.Remove(item);
                        Gallery.Prune();
                        Gallery.Store.State.ImageNotes.Remove(item.Path); Gallery.Store.Save();
                        break;
                }
            }
            catch (Exception error) { Gallery.Store.Log(error); Notice("操作未完成：" + error.Message); }
        }

        public void Dispose()
        {
            disposed = true; updateTimer.Stop(); updateLifetime.Cancel();
            if (Capture != null) Capture.Close();
            if (ShortcutSettings != null) ShortcutSettings.Close();
            if (updateWindow != null) updateWindow.Close();
            updates.Dispose();
            if (Basket != null) Basket.Close();
            if (TextWindow != null) TextWindow.Close();
            if (Appearance != null) Appearance.Close();
            if (notice != null) notice.Close();
            foreach (var editor in Editors.ToArray()) editor.CloseWithoutPrompt();
            TrayMenu.Close();
            Gallery.Changed -= SyncAutoHang;
            Texts.Changed -= Shelf.UpdateTexts;
            Captures.Dispose();
            events.Dispose();
            tray.Visible = false;
            var icon = tray.Icon;
            tray.Dispose();
            icon.Dispose();
            Shelf.Close();
        }

        async Task RefreshEditedImage(string path)
        {
            ImageContent image = await Task.Run(() => ImageContent.Read(path));
            var item = Gallery.Items.Find(candidate => FileRules.SameFile(candidate.Path, path));
            if (item == null) Gallery.Add(path, image);
            else
            {
                item.Image = image;
                item.Written = File.GetLastWriteTimeUtc(path);
                Gallery.Notify();
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        public static void Main(string[] arguments)
        {
            if (Array.IndexOf(arguments, "--quit") >= 0) { WindowsNative.RequestExit(); return; }
            bool first;
            using (var instance = new Mutex(true, @"Local\TangHao.ScreenLine.Windows", out first))
            {
                if (!first) { WindowsNative.ShowExisting(); return; }
                var store = new StateStore(StateStore.DefaultRoot);
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                LightTheme.Install(app);
                DesktopController controller = null;
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args) { store.Log(args.Exception); args.Handled = true; };
                app.Startup += async delegate { controller = new DesktopController(store, WindowsNative.ScreenshotFolder()); await controller.Start(); };
                app.Exit += delegate { if (controller != null) controller.Dispose(); };
                app.Run();
            }
        }
    }
}
