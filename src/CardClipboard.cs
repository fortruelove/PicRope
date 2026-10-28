using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ScreenLine {
internal sealed class ClipboardCard { public ClipboardPicture Picture; public string Text; }
internal static class CardClipboard {
    public static Task<ClipboardCard> Read(uint sequence, bool manual) {
        return WindowsClipboard.ReadOnSta(delegate {
            for(int attempt=0;attempt<8;attempt++) {
                if(WindowsNative.GetClipboardSequenceNumber()!=sequence) return null;
                try {
                    var data=Clipboard.GetDataObject();
                    var result=ReadData(data,manual).GetAwaiter().GetResult();
                    return WindowsNative.GetClipboardSequenceNumber()==sequence ? result : null;
                } catch(ExternalException) { if(attempt==7) throw; System.Threading.Thread.Sleep(60); }
            }
            return null;
        });
    }
    internal static async Task<ClipboardCard> ReadData(IDataObject data, bool manual) {
        if(data==null || (!manual && data.GetDataPresent(ClipboardRules.OwnFormat))) return null;
        var card=new ClipboardCard();
        card.Text=data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string;
        string extension; byte[] bytes=WindowsClipboard.ReadEncodedImage(data,out extension);
        if(bytes==null) {
            var bitmap=data.GetData(DataFormats.Bitmap) as BitmapSource;
            if(bitmap!=null) { bitmap.Freeze(); bytes=ImageContent.EncodeClipboard(bitmap); extension=".png"; }
        }
        if(bytes==null && manual && data.GetDataPresent(DataFormats.FileDrop)) {
            var files=data.GetData(DataFormats.FileDrop) as string[];
            if(files!=null && files.Length==1 && FileRules.IsImage(files[0])) {
                using(var file=new FileStream(files[0],FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) bytes=WindowsClipboard.ReadImageBytes(file);
                extension=Path.GetExtension(files[0]);
            }
        }
        if(bytes!=null && bytes.Length>0) {
            if(bytes.Length>64*1024*1024) throw new InvalidDataException("图片数据过大");
            // Validate before any file or record is added.
            await Task.Run(()=>ImageContent.Decode(bytes));
            card.Picture=new ClipboardPicture {Bytes=bytes,Extension=extension};
        }
        return card.Picture==null && string.IsNullOrWhiteSpace(card.Text) ? null : card;
    }
}
}
