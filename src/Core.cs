using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLine
{
    internal sealed class SavedState
    {
        public bool CollectClipboard { get; set; }
        public bool CollectTextClipboard { get; set; }
        public bool AutoClipboard { get; set; }
        public int ClipboardOptionsVersion { get; set; }
        public bool KeepRopeVisible { get; set; }
        public bool AutoTextHang { get; set; }
        public Dictionary<string, string> ImageNotes { get; set; }
        public List<string> CardOrder { get; set; }
        public List<string> DetachedPictures { get; set; }
        public bool AutoHang { get; set; }
        public bool HoverPreview { get; set; }
        public string Theme { get; set; }
        public string DisplayDevice { get; set; }
        public string LastUpdateNotification { get; set; }
        public uint CaptureModifiers { get; set; }
        public uint CaptureKey { get; set; }
        public List<string> Images { get; set; }
        public List<string> Collection { get; set; }
        public List<TextItem> Texts { get; set; }

        public SavedState() { Images = new List<string>(); Collection = new List<string>(); Texts = new List<TextItem>(); ImageNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); CardOrder = new List<string>(); DetachedPictures = new List<string>(); AutoTextHang = true; Theme = "classic"; AutoHang = true; HoverPreview = true; CaptureModifiers = 3; CaptureKey = (uint)'S'; }
    }

    internal sealed class StateStore
    {
        public static string DefaultRoot { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"); } }
        public readonly string Root;
        public readonly string Inbox;
        public readonly SavedState State;
        readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly string file;

        public StateStore(string root)
        {
            Root = Path.GetFullPath(root);
            Inbox = Path.Combine(Root, "Captures");
            file = Path.Combine(Root, "state.json");
            State = new SavedState();
            if (!File.Exists(file)) {State.ClipboardOptionsVersion=1;return;}
            try
            {
                State = serializer.Deserialize<SavedState>(File.ReadAllText(file, Encoding.UTF8)) ?? new SavedState();
                if (State.Images == null) State.Images = new List<string>();
                if (State.Collection == null) State.Collection = new List<string>();
                if (State.Texts == null) State.Texts = new List<TextItem>();
                State.ImageNotes = new Dictionary<string, string>(State.ImageNotes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                if (State.CardOrder == null) State.CardOrder = new List<string>();
                if (State.DetachedPictures == null) State.DetachedPictures = new List<string>();
                if(State.ClipboardOptionsVersion==0) {
                    if(State.AutoClipboard) {State.CollectClipboard=true;State.CollectTextClipboard=true;}
                    State.ClipboardOptionsVersion=1;
                }
            }
            catch (ArgumentException ex) { Log(ex); }
            catch (InvalidOperationException ex) { Log(ex); }
            catch (IOException ex) { Log(ex); }
        }

        public void Save()
        {
            Directory.CreateDirectory(Root);
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, serializer.Serialize(State), new UTF8Encoding(false));
            FileRules.Commit(temporary, file);
        }

        public void Log(Exception error)
        {
            Directory.CreateDirectory(Root);
            File.AppendAllText(Path.Combine(Root, "error.log"), DateTime.Now.ToString("s") + " " + error + Environment.NewLine);
        }
    }

    internal sealed class TextItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Content { get; set; }
        public DateTime CreatedUtc { get; set; }
        public bool Hanging { get; set; }
        bool marked;
        [System.Web.Script.Serialization.ScriptIgnore]
        public bool IsMarked
        {
            get { return marked; }
            set { if (marked == value) return; marked = value; if (PropertyChanged != null) PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs("IsMarked")); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class TextCollection
    {
        public readonly StateStore Store;
        public List<TextItem> Items { get { return Store.State.Texts; } }
        public event Action Changed;

        public TextCollection(StateStore store) { Store = store; }

        public bool AutoHang { get { return Store.State.AutoTextHang; } set { bool old = AutoHang; Store.State.AutoTextHang = value; try { Store.Save(); } catch { Store.State.AutoTextHang = old; throw; } Notify(); } }
        public void Notify() { if (Changed != null) Changed(); }
        public void SetHanging(IEnumerable<TextItem> selection, bool hanging)
        {
            var items = selection.Where(Items.Contains).ToArray(); var old = items.Select(t => t.Hanging).ToArray();
            var order=Store.State.CardOrder.ToList();
            foreach (var item in items) item.Hanging = hanging;
            foreach(var item in items) {string key="text:"+item.Id; if(hanging && !Store.State.CardOrder.Contains(key))Store.State.CardOrder.Add(key);}
            try { Store.Save(); } catch { Store.State.CardOrder=order;for (int i=0;i<items.Length;i++) items[i].Hanging=old[i]; throw; }
            Notify();
        }

        public bool CollectClipboard
        {
            get { return Store.State.CollectTextClipboard; }
            set
            {
                bool previous = CollectClipboard;
                if (previous == value) return;
                Store.State.CollectTextClipboard = value;
                try { Store.Save(); }
                catch { Store.State.CollectTextClipboard = previous; throw; }
                if (Changed != null) Changed();
            }
        }

        public TextItem Add(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            var existing = Items.Find(item => item.Content == content);
            if (existing != null) return existing;
            var added = new TextItem { Id = Guid.NewGuid().ToString("N"), Content = content, CreatedUtc = DateTime.UtcNow, Hanging = AutoHang };
            var order=Store.State.CardOrder.ToList();
            if(Store.State.CardOrder.Count==0) {Store.State.CardOrder.AddRange(Store.State.Images);Store.State.CardOrder.AddRange(Items.Where(t=>t.Hanging).Reverse().Select(t=>"text:"+t.Id));}
            if(added.Hanging)Store.State.CardOrder.Add("text:"+added.Id);
            Items.Insert(0, added);
            try { Store.Save(); }
            catch { Items.Remove(added); Store.State.CardOrder=order; throw; }
            if (Changed != null) Changed();
            return added;
        }

        public void Remove(TextItem item)
        {
            int index = Items.IndexOf(item);
            if (index < 0) return;
            Items.RemoveAt(index);
            try { Store.Save(); }
            catch { Items.Insert(index, item); throw; }
            if (Changed != null) Changed();
        }

        public void RemoveMany(IEnumerable<TextItem> selected)
        {
            var ids = new HashSet<string>(selected.Select(item => item.Id));
            var previous = Items.ToArray();
            if (Items.RemoveAll(item => ids.Contains(item.Id)) == 0) return;
            try { Store.Save(); }
            catch { Items.Clear(); Items.AddRange(previous); throw; }
            if (Changed != null) Changed();
        }
    }

    internal static class FileRules
    {
        static readonly string[] extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        public static void Commit(string temporary, string destination)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(destination)) File.Replace(temporary, destination, null);
                    else File.Move(temporary, destination);
                    return;
                }
                catch (IOException error)
                {
                    int code = error.HResult & 65535;
                    if (attempt == 4 || (code != 32 && code != 33 && code != 1175)) throw;
                    // 索引或杀毒扫描短暂占用时重试，仍以原子替换保护已有文件。
                    System.Threading.Thread.Sleep(30);
                }
            }
        }

        public static bool IsImage(string path)
        {
            return extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsInside(string path, string directory)
        {
            string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool SameFile(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class ClipboardRules
    {
        public const string OwnFormat = "PicRope.OwnClipboard";
        internal static readonly KeyValuePair<string, string>[] EncodedFormats = {
            new KeyValuePair<string, string>("PNG", ".png"), new KeyValuePair<string, string>("image/png", ".png"),
            new KeyValuePair<string, string>("image/jpeg", ".jpg"), new KeyValuePair<string, string>("image/jpg", ".jpg"), new KeyValuePair<string, string>("image/jpe", ".jpg"),
            new KeyValuePair<string, string>("image/bmp", ".bmp"), new KeyValuePair<string, string>("image/gif", ".gif"),
            new KeyValuePair<string, string>("image/tiff", ".tiff"), new KeyValuePair<string, string>("image/tif", ".tiff")
        };
        static readonly string[] excluded = { "HTML Format", "UnicodeText", "Text", "System.String", "FileDrop", "Rich Text Format" };
        static readonly TimeSpan htmlTimeout = TimeSpan.FromMilliseconds(200);

        public static bool IsPlainImage(IEnumerable<string> formats)
        {
            var values = new HashSet<string>(formats, StringComparer.OrdinalIgnoreCase);
            return !values.Overlaps(excluded) && HasImage(values);
        }

        static bool HasImage(HashSet<string> formats)
        {
            return formats.Contains("Bitmap") || formats.Contains("DeviceIndependentBitmap") || formats.Contains("Format17")
                || EncodedFormats.Any(format => formats.Contains(format.Key));
        }

        public static bool IsImage(IDataObject data) { return IsImage(data, null); }

        public static bool IsImage(IDataObject data, string source)
        {
            string imageFile;
            return IsImage(data, source, out imageFile);
        }

        public static bool IsImage(IDataObject data, string source, out string imageFile)
        {
            imageFile = null;
            var formats = new HashSet<string>(data.GetFormats(), StringComparer.OrdinalIgnoreCase);
            if (formats.Contains(OwnFormat)) return false;
            if (IsPlainImage(formats)) return true;
            if (formats.Contains("FileDrop"))
            {
                bool wechat = string.Equals(source, "Weixin", StringComparison.OrdinalIgnoreCase) || string.Equals(source, "WeChat", StringComparison.OrdinalIgnoreCase);
                if (!wechat || !formats.Contains("image/png") || !formats.Contains("DeviceIndependentBitmap")
                    || formats.Contains("HTML Format") || formats.Contains("Rich Text Format") || formats.Contains("UnicodeText") || formats.Contains("Text") || formats.Contains("System.String")) return false;
                var files = data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length != 1 || !FileRules.IsImage(files[0])) return false;
                imageFile = files[0]; return true;
            }
            if (formats.Contains("Rich Text Format") || !formats.Contains("HTML Format") || !HasImage(formats)) return false;
            string html = data.GetData(DataFormats.Html) as string;
            if (string.IsNullOrEmpty(html) || html.Length > 1024 * 1024) return false;
            const string start = "<!--StartFragment-->", end = "<!--EndFragment-->";
            int first = html.IndexOf(start, StringComparison.OrdinalIgnoreCase), last = html.IndexOf(end, StringComparison.OrdinalIgnoreCase);
            if (first >= 0 || last >= 0)
            {
                if (first < 0 || last < first + start.Length) return false;
                html = html.Substring(first + start.Length, last - first - start.Length);
            }
            else
            {
                int document = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
                if (document >= 0) html = html.Substring(document);
            }
            try
            {
                var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
                html = Regex.Replace(html, @"<!--[\s\S]*?-->", "", options, htmlTimeout);
                const string tail = @"(?:[^'"">]|""[^""]*""|'[^']*')*";
                var images = Regex.Matches(html, @"<\s*img\b" + tail + @">", options, htmlTimeout);
                if (images.Count != 1) return false;
                string text = data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string ?? data.GetData("System.String") as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var imageSource = Regex.Match(images[0].Value, @"\ssrc\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)'|(?<url>[^\s>]+))", options, htmlTimeout);
                    if (!imageSource.Success || !string.Equals(text.Trim(), WebUtility.HtmlDecode(imageSource.Groups["url"].Value), StringComparison.Ordinal)) return false;
                }
                html = html.Remove(images[0].Index, images[0].Length);
                html = Regex.Replace(html, @"<\s*/?\s*(?:html|body|div|span|p|a|figure|br)\b" + tail + @">", "", options, htmlTimeout);
                return string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(html));
            }
            catch (RegexMatchTimeoutException) { return false; }
        }

    }

    internal sealed class ImageContent
    {
        static readonly LinkedList<ImageContent> cache = new LinkedList<ImageContent>();
        BitmapSource thumbnail;
        string backingPath;
        LinkedListNode<ImageContent> cacheNode;
        internal BitmapSource CachedThumbnail
        {
            get
            {
                if (!System.Threading.Monitor.TryEnter(cache)) return null;
                try { if (thumbnail != null) Touch(); return thumbnail; }
                finally { System.Threading.Monitor.Exit(cache); }
            }
        }
        public BitmapSource Thumbnail
        {
            get
            {
                string path;
                lock (cache)
                {
                    if (thumbnail != null) { Touch(); return thumbnail; }
                    path = backingPath;
                }
                BitmapSource loaded;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    loaded = MakeThumbnail(stream, PixelWidth, PixelHeight);
                lock (cache)
                {
                    if (thumbnail == null) thumbnail = loaded;
                    Touch(); return thumbnail;
                }
            }
        }
        public readonly string Fingerprint;
        public readonly int PixelWidth, PixelHeight;

        ImageContent(BitmapSource thumbnail, string fingerprint, int width, int height)
        {
            this.thumbnail = thumbnail;
            Fingerprint = fingerprint;
            PixelWidth = width;
            PixelHeight = height;
        }

        internal void BindFile(string path)
        {
            lock (cache) { backingPath = path; if (thumbnail != null) Touch(); }
        }

        internal ImageContent ForFile(string path)
        {
            lock (cache)
            {
                var image = backingPath != null && !FileRules.SameFile(backingPath, path)
                    ? new ImageContent(thumbnail, Fingerprint, PixelWidth, PixelHeight) : this;
                image.BindFile(path); return image;
            }
        }

        void Touch()
        {
            if (backingPath == null) return;
            if (cacheNode != null) cache.Remove(cacheNode);
            cacheNode = cache.AddLast(this);
            while (cache.Count > 64)
            {
                var evicted = cache.First.Value; cache.RemoveFirst();
                evicted.cacheNode = null; evicted.thumbnail = null;
            }
        }

        public static ImageContent Read(string path)
        {
            return Read(path, true);
        }

        public static ImageContent Read(string path, bool fingerprint)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length == 0 || stream.Length > 64L * 1024 * 1024) throw new InvalidDataException("Image file size is invalid");
                if (!fingerprint)
                {
                    var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                    if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 80000000) throw new InvalidDataException("Image dimensions are invalid");
                    var metadata = new ImageContent(null, null, frame.PixelWidth, frame.PixelHeight); metadata.BindFile(path); return metadata;
                }
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    var image = Decode(memory.ToArray(), fingerprint); image.BindFile(path); return image;
                }
            }
        }

        public static ImageContent Decode(byte[] bytes)
        {
            return Decode(bytes, true);
        }

        static ImageContent Decode(byte[] bytes, bool fingerprint)
        {
            using (var memory = new MemoryStream(bytes))
            {
                var full = BitmapDecoder.Create(memory, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                if (full.PixelWidth <= 0 || full.PixelHeight <= 0 || (long)full.PixelWidth * full.PixelHeight > 80000000)
                    throw new InvalidDataException("Image dimensions are invalid");
                string hash = fingerprint ? HashPixels(full) : null;
                memory.Position = 0;
                return new ImageContent(MakeThumbnail(memory, full.PixelWidth, full.PixelHeight), hash, full.PixelWidth, full.PixelHeight);
            }
        }

        static BitmapSource MakeThumbnail(Stream stream, int width, int height)
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
            if (width >= height) image.DecodePixelWidth = Math.Min(240, width); else image.DecodePixelHeight = Math.Min(240, height);
            image.EndInit();
            var color = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = color.PixelWidth * 4; byte[] pixels = new byte[stride * color.PixelHeight];
            color.CopyPixels(pixels, stride, 0);
            var detached = BitmapSource.Create(color.PixelWidth, color.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            detached.Freeze(); return detached;
        }

        public static byte[] EncodeClipboard(BitmapSource image)
        {
            // 部分 Windows 位图的透明通道全为零，截图应按不透明图像保存。
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] rows = new byte[stride * Math.Min(32, converted.PixelHeight)];
            bool emptyAlpha = true;
            for (int y = 0; y < converted.PixelHeight && emptyAlpha; y += 32)
            {
                int height = Math.Min(32, converted.PixelHeight - y);
                converted.CopyPixels(new Int32Rect(0, y, converted.PixelWidth, height), rows, stride, 0);
                for (int i = 3; i < stride * height; i += 4)
                    if (rows[i] != 0) { emptyAlpha = false; break; }
            }
            if (emptyAlpha)
            {
                byte[] pixels = new byte[stride * converted.PixelHeight];
                converted.CopyPixels(pixels, stride, 0);
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                image = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var memory = new MemoryStream()) { encoder.Save(memory); return memory.ToArray(); }
        }

        public static string HashPixels(BitmapSource image)
        {
            var color = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = color.PixelWidth * 4;
            byte[] header = new byte[8];
            BitConverter.GetBytes(color.PixelWidth).CopyTo(header, 0);
            BitConverter.GetBytes(color.PixelHeight).CopyTo(header, 4);
            byte[] pixels = new byte[stride * Math.Min(32, color.PixelHeight)];
            using (var sha = SHA256.Create())
            {
                sha.TransformBlock(header, 0, header.Length, header, 0);
                for (int y = 0; y < color.PixelHeight; y += 32)
                {
                    int height = Math.Min(32, color.PixelHeight - y);
                    color.CopyPixels(new Int32Rect(0, y, color.PixelWidth, height), pixels, stride, 0);
                    sha.TransformBlock(pixels, 0, stride * height, pixels, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return Convert.ToBase64String(sha.Hash);
            }
        }
    }

    internal enum CaptureSource { Folder, Clipboard }

    internal sealed class CaptureReceipts
    {
        sealed class Receipt
        {
            public string Hash;
            public CaptureSource Source;
            public DateTime Time;
            public bool Paired;
        }

        readonly List<Receipt> recent = new List<Receipt>();

        public bool Accept(string hash, CaptureSource source, DateTime time)
        {
            recent.RemoveAll(r => time - r.Time > TimeSpan.FromSeconds(5));
            // 只合并文件夹与剪贴板对同一次截图的通知；连续截取相同内容仍可保留。
            var pair = recent.FirstOrDefault(r => !r.Paired && r.Hash == hash && r.Source != source);
            if (pair != null) { pair.Paired = true; return false; }
            recent.Add(new Receipt { Hash = hash, Source = source, Time = time });
            return true;
        }
    }

    internal sealed class CaptureItem
    {
        public readonly string Path;
        public ImageContent Image;
        public DateTime Written;
        public TextItem Text;
        public string Note = "";
        public bool IsText { get { return Text != null; } }
        public string Label { get { return IsText ? Text.Content : System.IO.Path.GetFileName(Path) + (string.IsNullOrEmpty(Note) ? "" : " · " + Note.Replace("\n", " ")); } }

        public CaptureItem(TextItem text) { Text = text; Path = "text:" + text.Id; Written = text.CreatedUtc; }

        public CaptureItem(string path, ImageContent image)
        {
            Path = System.IO.Path.GetFullPath(path);
            Image = image.ForFile(Path);
            Written = File.GetLastWriteTimeUtc(Path);
        }
    }

    internal sealed class Gallery
    {
        public readonly List<CaptureItem> Items = new List<CaptureItem>();
        public readonly List<CaptureItem> Collected = new List<CaptureItem>();
        public readonly StateStore Store;
        public event Action Changed;

        public Gallery(StateStore store) { Store = store; }

        readonly Dictionary<string, CaptureItem> textCards = new Dictionary<string, CaptureItem>();
        public List<CaptureItem> Hanging
        {
            get
            {
                var result = Items.ToList();
                foreach (var text in Store.State.Texts.Where(t => t.Hanging))
                {
                    CaptureItem card;
                    if (!textCards.TryGetValue(text.Id, out card) || card.Text != text) textCards[text.Id] = card = new CaptureItem(text);
                    result.Add(card);
                }
                foreach (var id in textCards.Keys.Where(id => !Store.State.Texts.Any(t => t.Id == id)).ToArray()) textCards.Remove(id);
                foreach (var item in result.Where(i => !i.IsText)) item.Note = Note(item);
                var order = Store.State.CardOrder;
                return result.OrderBy(i => { int n=order.IndexOf(i.Path); return n < 0 ? int.MaxValue : n; }).ToList();
            }
        }
        public string Note(CaptureItem item) { string note; return Store.State.ImageNotes.TryGetValue(item.Path, out note) ? note : ""; }
        public bool Reorder(string source, int insertion, IEnumerable<string> visible, bool onRope)
        {
            if (!onRope) return false;
            var all = Hanging.Select(i => i.Path).ToList();
            var valid = new HashSet<string>(all, StringComparer.OrdinalIgnoreCase);
            var keys = visible.Where(valid.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            int from = keys.FindIndex(key => string.Equals(key, source, StringComparison.OrdinalIgnoreCase));
            if (from < 0) return false;
            insertion = Math.Max(0, Math.Min(keys.Count, insertion));
            keys.RemoveAt(from);
            if (from < insertion) insertion--;
            keys.Insert(insertion, source);
            var shown = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            int next = 0;
            var reordered = all.Select(key => shown.Contains(key) ? keys[next++] : key).ToList();
            // Keep not-yet-restored images and temporarily unmounted cards in the persisted order.
            var saved = Store.State.CardOrder;
            ChangeCards(delegate { Store.State.CardOrder = reordered.Concat(saved).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); });
            return true;
        }
        public void SetNote(CaptureItem item, string note)
        {
            if (!Collected.Contains(item)) return;
            ChangeCards(delegate { if (string.IsNullOrEmpty(note)) Store.State.ImageNotes.Remove(item.Path); else Store.State.ImageNotes[item.Path] = note; });
        }
        // Related records are changed in one state-file write; failed writes restore all relationships.
        void ChangeCards(Action change)
        {
            var notes = new Dictionary<string,string>(Store.State.ImageNotes, StringComparer.OrdinalIgnoreCase);
            var texts = Store.State.Texts.ToList(); var hanging = texts.Select(t=>t.Hanging).ToArray(); var contents=texts.Select(t=>t.Content).ToArray();
            var images=Items.ToList(); var collected=Collected.ToList(); var order=Store.State.CardOrder.ToList(); var detached=Store.State.DetachedPictures.ToList();
            try { change(); Save(); }
            catch { Store.State.ImageNotes=notes; Store.State.Texts=texts; for(int i=0;i<texts.Count;i++) {texts[i].Hanging=hanging[i];texts[i].Content=contents[i];} Items.Clear(); Items.AddRange(images); Collected.Clear(); Collected.AddRange(collected); Store.State.CardOrder=order; Store.State.DetachedPictures=detached; Store.State.Images=images.Select(i=>i.Path).ToList(); Store.State.Collection=collected.Select(i=>i.Path).ToList(); throw; }
            foreach(var item in Collected) item.Note=Note(item);
            Notify();
        }
        public void AttachText(CaptureItem picture, TextItem text, bool keepTextPosition, bool append)
        {
            if (!Collected.Contains(picture) || !Store.State.Texts.Contains(text)) return;
            ChangeCards(delegate {
                var order=Hanging.Select(i=>i.Path).ToList(); string key="text:"+text.Id;
                if (keepTextPosition) {
                    Items.Remove(picture); if(text.Hanging) Items.Add(picture);
                    order.Remove(picture.Path); int n=order.IndexOf(key); if(n>=0) order[n]=picture.Path;
                }
                else order.Remove(key);
                Store.State.CardOrder=order;
                string old=Note(picture); Store.State.ImageNotes[picture.Path]=append && old.Length>0 ? old+"\n"+text.Content : text.Content;
                Store.State.Texts.Remove(text);
            });
        }
        public void Split(CaptureItem picture, TextCollection texts)
        {
            string note=Note(picture); if(string.IsNullOrEmpty(note)) return;
            ChangeCards(delegate {
                var order=Hanging.Select(i=>i.Path).ToList();
                var text=new TextItem {Id=Guid.NewGuid().ToString("N"),Content=note,CreatedUtc=DateTime.UtcNow,Hanging=Items.Contains(picture)};
                Store.State.Texts.Insert(0,text); Store.State.ImageNotes.Remove(picture.Path);
                int n=order.IndexOf(picture.Path); if(n>=0) order.Insert(n+1,"text:"+text.Id); Store.State.CardOrder=order;
            }); texts.Notify();
        }
        public void RemovePicture(CaptureItem picture, TextCollection texts)
        {
            string note=Note(picture);
            ChangeCards(delegate {
                var order=Hanging.Select(i=>i.Path).ToList(); int n=order.IndexOf(picture.Path);
                if (!string.IsNullOrEmpty(note)) { var text=new TextItem {Id=Guid.NewGuid().ToString("N"),Content=note,CreatedUtc=DateTime.UtcNow,Hanging=Items.Contains(picture)}; Store.State.Texts.Insert(0,text); if(n>=0) order[n]="text:"+text.Id; }
                else order.Remove(picture.Path);
                Store.State.ImageNotes.Remove(picture.Path); Items.Remove(picture); Collected.Remove(picture); Store.State.CardOrder=order; Store.State.DetachedPictures.Add(picture.Path);
            }); texts.Notify();
        }
        public void SaveCard(CaptureItem original, CaptureItem picture, string content, TextItem consumed, bool append, bool newPicture = false)
        {
            if (original.IsText && !Store.State.Texts.Contains(original.Text)) throw new InvalidOperationException("文字已被移除，请重新打开。");
            if (picture != null && !Collected.Contains(picture) && !newPicture) throw new InvalidOperationException("配图已被移除，请重新选择。");
            if (picture == null && original.IsText && string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("文字不能为空。");
            ChangeCards(delegate {
                if (original.IsText && picture == null) { original.Text.Content=content; return; }
                var target=picture ?? original;
                bool mounted=original.IsText ? original.Text.Hanging : Items.Contains(original);
                var order=Hanging.Select(i=>i.Path).ToList();
                string old=Note(target);
                Store.State.ImageNotes[target.Path]=append && !string.IsNullOrEmpty(old) ? old+"\n"+content : content;
                if(original.IsText) Store.State.Texts.Remove(original.Text);
                else if(target!=original) {Items.Remove(original);Collected.Remove(original);Store.State.ImageNotes.Remove(original.Path);Store.State.DetachedPictures.Add(original.Path);}
                if(!Collected.Contains(target))Collected.Add(target);
                if(consumed!=null) Store.State.Texts.Remove(consumed);
                if(target.Path!=original.Path) {order.Remove(target.Path);int n=order.IndexOf(original.Path);if(n>=0)order[n]=target.Path;}
                if(consumed!=null)order.Remove("text:"+consumed.Id);
                Items.Remove(target); if(mounted)Items.Add(target);
                Store.State.CardOrder=order;
            });
        }
        public CaptureItem ImportCard(string path, ImageContent image, string note, bool hanging)
        {
            var item = new CaptureItem(path, image);
            ChangeCards(delegate {
                Collected.Add(item); if(hanging){Items.Add(item);if(Items.Count>50)Items.RemoveAt(0);}
                if(!string.IsNullOrWhiteSpace(note))Store.State.ImageNotes[item.Path]=note;
                Store.State.DetachedPictures.RemoveAll(p=>string.Equals(p,item.Path,StringComparison.OrdinalIgnoreCase));
            });
            return item;
        }
        public void HangCard(CaptureItem item) { if(Contains(item.Path))return;ChangeCards(delegate{Items.Add(item);if(Items.Count>50)Items.RemoveAt(0);}); }

        public bool Contains(string path) { return Items.Any(i => FileRules.SameFile(i.Path, path)); }

        public CaptureItem Collect(string path, ImageContent image)
        {
            var item = Collected.FirstOrDefault(i => FileRules.SameFile(i.Path, path));
            if (item != null) return item;
            item = new CaptureItem(path, image);
            Collected.Add(item);
            Save();
            Notify();
            return item;
        }

        public void Add(string path, ImageContent image)
        {
            if (Contains(path)) return;
            var item = Collected.FirstOrDefault(i => FileRules.SameFile(i.Path, path));
            if (item == null) { item = new CaptureItem(path, image); Collected.Add(item); }
            else if (item.Image != image) { item.Image = image.ForFile(path); item.Written = File.GetLastWriteTimeUtc(path); }
            Items.Add(item);
            if (Items.Count > 50) Items.RemoveAt(0);
            Save();
            Notify();
        }

        public void Remove(CaptureItem item)
        {
            Items.Remove(item);
            Save();
            Notify();
        }

        public void Clear()
        {
            Items.Clear();
            Save();
            Notify();
        }

        public void Prune()
        {
            int removed = Items.RemoveAll(i => !File.Exists(i.Path));
            removed += Collected.RemoveAll(i => !File.Exists(i.Path));
            if (removed == 0) return;
            Save();
            Notify();
        }

        public void Notify() { if (Changed != null) Changed(); }

        void Save()
        {
            Store.State.Images = Items.Select(i => i.Path).ToList();
            Store.State.Collection = Collected.Select(i => i.Path).ToList();
            foreach(var key in Items.Select(i=>i.Path).Concat(Store.State.Texts.Where(t=>t.Hanging).Reverse().Select(t=>"text:"+t.Id)))
                if(!Store.State.CardOrder.Contains(key))Store.State.CardOrder.Add(key);
            Store.Save();
        }
    }
}
