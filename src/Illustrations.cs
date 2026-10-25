using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace Rawer {
internal static class ComicArt {
    static readonly Dictionary<string,Bitmap> images=new Dictionary<string,Bitmap>();static readonly object gate=new object();
    public static Bitmap Get(string key){
        lock(gate){Bitmap cached;if(images.TryGetValue(key,out cached))return cached;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Rawer.Art."+key+".png")){
                if(stream==null)throw new InvalidOperationException("Embedded comic artwork is missing: "+key);
                using(var original=Image.FromStream(stream)){
                    double scale=Math.Min(1,384.0/Math.Max(original.Width,original.Height));int w=Math.Max(1,(int)(original.Width*scale)),h=Math.Max(1,(int)(original.Height*scale));
                    cached=new Bitmap(w,h,PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(cached)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.CompositingQuality=CompositingQuality.HighQuality;g.DrawImage(original,new Rectangle(0,0,w,h));}
                }
            }
            images.Add(key,cached);return cached;
        }
    }
    public static void Draw(Graphics g,string key,Rectangle bounds){
        var image=Get(key);double scale=Math.Min((double)bounds.Width/image.Width,(double)bounds.Height/image.Height);int w=Math.Max(1,(int)(image.Width*scale)),h=Math.Max(1,(int)(image.Height*scale));
        g.DrawImage(image,new Rectangle(bounds.X+(bounds.Width-w)/2,bounds.Y+(bounds.Height-h)/2,w,h));
    }
}
internal sealed class ComicIllustration : System.Windows.Forms.Control {
    readonly string key;Bitmap cached;
    public ComicIllustration(string key){this.key=key;DoubleBuffered=true;BackColor=Comic.White;SetStyle(System.Windows.Forms.ControlStyles.Opaque|System.Windows.Forms.ControlStyles.ResizeRedraw,true);}
    protected override void OnSizeChanged(EventArgs e){if(cached!=null){cached.Dispose();cached=null;}base.OnSizeChanged(e);}
    protected override void OnPaint(System.Windows.Forms.PaintEventArgs e){
        if(Width<2||Height<2)return;if(cached==null){cached=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(cached)){g.Clear(BackColor);ComicArt.Draw(g,key,new Rectangle(0,0,Width,Height));}}e.Graphics.DrawImageUnscaled(cached,0,0);
    }
    protected override void Dispose(bool disposing){if(disposing&&cached!=null)cached.Dispose();base.Dispose(disposing);}
}
}
