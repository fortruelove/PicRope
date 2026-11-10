using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Net;
using System.Text.RegularExpressions;

namespace ScreenLine {
internal sealed class ClipboardCard { public ClipboardPicture Picture; public ClipboardPicture[] AdditionalPictures; public string Text; }
internal static class CardClipboard {
    public static Task<ClipboardCard> Read(uint sequence, bool manual, bool pictures = true, bool texts = true, bool multiple = false) {
        return WindowsClipboard.ReadOnSta(delegate {
            for(int attempt=0;attempt<8;attempt++) {
                if(WindowsNative.GetClipboardSequenceNumber()!=sequence) return null;
                try {
                    var result=ReadData(Clipboard.GetDataObject(),manual,pictures,texts,multiple).GetAwaiter().GetResult();
                    return WindowsNative.GetClipboardSequenceNumber()==sequence ? result : null;
                } catch(ExternalException) { if(attempt==7) throw; System.Threading.Thread.Sleep(60); }
            }
            return null;
        });
    }
    // Keep all OLE reads on one STA; isolate failures of optional formats.
    internal static Task<ClipboardCard> ReadData(IDataObject data, bool manual, bool pictures = true, bool texts = true, bool multiple = false) {
        return Task.FromResult(ReadSnapshot(data,manual,pictures,texts,multiple));
    }
    static object Get(IDataObject data,string format) {
        try {return data.GetDataPresent(format) ? data.GetData(format) : null;}
        catch(ExternalException) {return null;}
        catch(ArgumentException) {return null;}
        catch(NotSupportedException) {return null;}
        catch(IOException) {return null;}
    }
    static ClipboardCard ReadSnapshot(IDataObject data,bool manual,bool pictures,bool texts,bool multiple) {
        if(data==null || (!manual && Get(data,ClipboardRules.OwnFormat)!=null))return null;
        var card=new ClipboardCard {Text=texts ? Get(data,DataFormats.UnicodeText) as string ?? Get(data,DataFormats.Text) as string : null};
        if(!pictures)return string.IsNullOrWhiteSpace(card.Text)?null:card;
        var files = Get(data, DataFormats.FileDrop) as string[];
        if (multiple) {
            var batch = files != null && files.Length > 1
                ? ReadBatch(files.Where(FileRules.IsImage), false)
                : new List<ClipboardPicture>();
            if (batch.Count == 0) {
                var paths = HtmlPaths(Get(data, DataFormats.Html) as string, true);
                if (paths.Length > 1) batch = ReadBatch(paths, true);
            }
            if (batch.Count > 0) {
                card.Picture = batch[0];
                card.AdditionalPictures = batch.Skip(1).ToArray();
                return card;
            }
        }
        foreach(var format in ClipboardRules.EncodedFormats) {
            try {
                byte[] bytes=Bytes(Get(data,format.Key));
                if(Valid(bytes)) {card.Picture=new ClipboardPicture{Bytes=bytes,Extension=format.Value};break;}
            } catch(InvalidDataException) {} catch(IOException) {} catch(ExternalException) {} catch(NotSupportedException) {} catch(InvalidOperationException) {}
        }
        if(card.Picture==null) {
            try {
                var raw=Get(data,DataFormats.Bitmap);
                var bitmap=raw as BitmapSource;
                if(bitmap!=null) {bitmap.Freeze();card.Picture=Png(ImageContent.EncodeClipboard(bitmap));}
                else {
                    var drawing=raw as System.Drawing.Image;
                    if(drawing!=null)using(var stream=new MemoryStream()){drawing.Save(stream,System.Drawing.Imaging.ImageFormat.Png);card.Picture=Png(stream.ToArray());}
                }
            } catch(ArgumentException) {} catch(NotSupportedException) {} catch(ExternalException) {} catch(IOException) {}
        }
        if(card.Picture==null)foreach(string format in new[]{"Format17",DataFormats.Dib}) {
            try {
                byte[] bmp=FromDib(Bytes(Get(data,format)));
                if(Valid(bmp)) {card.Picture=new ClipboardPicture{Bytes=bmp,Extension=".bmp"};break;}
            } catch(InvalidDataException) {} catch(IOException) {} catch(ExternalException) {}
        }
        if (card.Picture == null && files != null && files.Length == 1 && FileRules.IsImage(files[0]))
            card.Picture = ReadLocalPicture(files[0], false);
        if (card.Picture == null) {
            var paths = HtmlPaths(Get(data, DataFormats.Html) as string, false);
            if (paths.Length == 1) card.Picture = ReadLocalPicture(paths[0], true);
        }
        return card.Picture==null && string.IsNullOrWhiteSpace(card.Text) ? null : card;
    }
    static List<ClipboardPicture> ReadBatch(IEnumerable<string> paths, bool encodePng) {
        var batch = new List<ClipboardPicture>();
        long bytes = 0;
        foreach (var path in paths.Take(100)) {
            var picture = ReadLocalPicture(path, encodePng);
            if (picture == null) continue;
            if (bytes + picture.Bytes.Length > 128L * 1024 * 1024) break;
            batch.Add(picture);
            bytes += picture.Bytes.Length;
        }
        return batch;
    }

    // QQ can expose cached local images as CF_HTML. Never fetch remote or network paths.
    static string[] HtmlPaths(string html, bool multiple) {
        if (string.IsNullOrEmpty(html) || html.Length > 1024 * 1024) return new string[0];
        try {
            var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            var timeout = TimeSpan.FromMilliseconds(200);
            const string tail = @"(?:[^'"">]|""[^""]*""|'[^']*')*";
            var images = Regex.Matches(html, @"<\s*img\b" + tail + @">", options, timeout);
            if (!multiple && images.Count != 1) return new string[0];
            var paths = new List<string>();
            foreach (Match image in images) {
                if (paths.Count == 100) break;
                var source = Regex.Match(image.Value, @"\ssrc\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)'|(?<url>[^\s>]+))", options, timeout);
                Uri uri;
                if (source.Success && Uri.TryCreate(WebUtility.HtmlDecode(source.Groups["url"].Value), UriKind.Absolute, out uri)
                    && uri.IsFile && !uri.IsUnc && !uri.LocalPath.StartsWith(@"\\", StringComparison.Ordinal))
                    paths.Add(uri.LocalPath);
            }
            return paths.ToArray();
        } catch (RegexMatchTimeoutException) { return new string[0]; }
    }

    static ClipboardPicture ReadLocalPicture(string path, bool encodePng) {
        try {
            byte[] bytes;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                bytes = WindowsClipboard.ReadImageBytes(file);
            if (!Valid(bytes)) return null;
            if (!encodePng) return new ClipboardPicture { Bytes = bytes, Extension = Path.GetExtension(path) };
            using (var stream = new MemoryStream(bytes)) {
                var picture = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                return Png(ImageContent.EncodeClipboard(picture));
            }
        } catch (IOException) {} catch (UnauthorizedAccessException) {} catch (ArgumentException) {}
          catch (NotSupportedException) {} catch (ExternalException) {}
        return null;
    }
    static ClipboardPicture Png(byte[] bytes) {return Valid(bytes)?new ClipboardPicture{Bytes=bytes,Extension=".png"}:null;}
    static byte[] Bytes(object value) {var bytes=value as byte[];if(bytes!=null)return bytes;var stream=value as Stream;return stream==null?null:WindowsClipboard.ReadImageBytes(stream);}
    static bool Valid(byte[] bytes) {
        if(bytes==null || bytes.Length==0 || bytes.Length>64*1024*1024)return false;
        try {ImageContent.Decode(bytes);return true;}
        catch(NotSupportedException){return false;}catch(ArgumentException){return false;}catch(IOException){return false;}catch(InvalidOperationException){return false;}catch(ExternalException){return false;}
    }
    internal static byte[] FromDib(byte[] dib) {
        if(dib==null || dib.Length<40 || dib.Length>64*1024*1024)return null;
        uint header=BitConverter.ToUInt32(dib,0); if(header<40 || header>dib.Length)return null;
        int bits=BitConverter.ToUInt16(dib,14);uint compression=BitConverter.ToUInt32(dib,16),colors=BitConverter.ToUInt32(dib,32);
        if(colors==0 && bits<=8)colors=(uint)(1<<bits);
        long offset=14L+header+colors*4L+(header==40 && (compression==3 || compression==6)?(compression==6?16:12):0);
        if(offset>dib.Length+14L)return null;
        var bmp=new byte[dib.Length+14];bmp[0]=(byte)'B';bmp[1]=(byte)'M';BitConverter.GetBytes(bmp.Length).CopyTo(bmp,2);BitConverter.GetBytes((int)offset).CopyTo(bmp,10);Buffer.BlockCopy(dib,0,bmp,14,dib.Length);return bmp;
    }
    internal static ClipboardCard ForAutomatic(ClipboardCard card,bool pictures,bool texts) {
        if(card==null)return null;
        return card.Picture!=null && pictures ? new ClipboardCard{Picture=card.Picture,Text=texts?card.Text:null} : texts && !string.IsNullOrWhiteSpace(card.Text) ? new ClipboardCard{Text=card.Text} : null;
    }
}
}
