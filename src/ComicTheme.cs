using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Rawer {
internal static class Comic {
    public static readonly Color Ink=Color.FromArgb(27,23,31),Paper=Color.FromArgb(255,251,239),White=Color.FromArgb(255,254,247),Yellow=Color.FromArgb(255,222,30),Orange=Color.FromArgb(255,90,103),Mint=Color.FromArgb(78,215,91),Blue=Color.FromArgb(40,181,238),Purple=Color.FromArgb(192,77,219),Muted=Color.FromArgb(94,87,97);
    static readonly Bitmap dots=MakeDots();
    static Bitmap MakeDots(){var b=new Bitmap(9,9,PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(b)){g.Clear(Color.Transparent);using(var fill=new SolidBrush(Color.FromArgb(43,Ink)))g.FillEllipse(fill,1,1,2.5f,2.5f);}return b;}
    public static void Dots(Graphics g,Rectangle r){using(var brush=new TextureBrush(dots,WrapMode.Tile))g.FillRectangle(brush,r);}
    public static void Text(Graphics g,string text,Font font,Color color,Rectangle rect,TextFormatFlags extra){TextRenderer.DrawText(g,text,font,rect,color,extra|TextFormatFlags.NoPadding);}
    public static void Headline(Graphics g,string text,Rectangle rect,float size,Color fill){
        using(var path=new GraphicsPath())using(var family=new FontFamily("Impact"))using(var format=new StringFormat{Alignment=StringAlignment.Near,LineAlignment=StringAlignment.Center}){
            path.AddString(text,family,(int)FontStyle.Italic,size,rect,format);
            using(var pen=new Pen(Ink,1.6f){LineJoin=LineJoin.Round})using(var brush=new SolidBrush(fill)){g.DrawPath(pen,path);g.FillPath(brush,path);}
        }
    }
    public static GraphicsPath Balloon(RectangleF r,bool tail){
        var p=new GraphicsPath();float x=r.X,y=r.Y,w=r.Width,h=r.Height,t=tail?10:0;
        p.AddBezier(x+8,y+3,x+w*.28f,y-2,x+w*.75f,y+1,x+w-8,y+3);
        p.AddBezier(x+w-8,y+3,x+w+2,y+11,x+w-2,y+h*.7f,x+w-7,y+h-t-5);
        p.AddBezier(x+w-7,y+h-t-5,x+w*.82f,y+h-t+2,x+w*.73f,y+h-t-1,x+w*.66f,y+h-t);
        if(tail){p.AddLine(x+w*.66f,y+h-t,x+w*.57f,y+h+2);p.AddLine(x+w*.57f,y+h+2,x+w*.55f,y+h-t);}
        p.AddBezier(x+w*.55f,y+h-t,x+w*.24f,y+h-t+3,x+10,y+h-t-1,x+5,y+h-t-5);
        p.AddBezier(x+5,y+h-t-5,x-2,y+h*.6f,x,y+12,x+8,y+3);p.CloseFigure();return p;
    }
    public static void Box(Graphics g,Rectangle r,Color fill,int shadow=3){
        if(r.Width<3||r.Height<3)return;
        var points=new[]{new Point(r.Left,r.Top+1),new Point(r.Right,r.Top),new Point(r.Right-1,r.Bottom),new Point(r.Left+1,r.Bottom-1)};
        if(shadow>0){var state=g.Save();g.TranslateTransform(shadow,shadow);using(var b=new SolidBrush(Ink))g.FillPolygon(b,points);g.Restore(state);}
        using(var b=new SolidBrush(fill))using(var p=new Pen(Ink,2.2f){LineJoin=LineJoin.Round}){g.FillPolygon(b,points);g.DrawPolygon(p,points);}
    }
}
internal sealed class ComicPaper : Panel {
    public ComicPaper(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Comic.Paper;AutoScroll=true;}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Comic.Paper);}
}
internal sealed class ComicPageLayout : TableLayoutPanel {
    public ComicPageLayout(){DoubleBuffered=true;BackColor=Comic.Paper;}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Comic.Paper);using(var p=new Pen(Color.FromArgb(226,35,55),7))e.Graphics.DrawRectangle(p,4,4,Width-8,Height-8);}
}
internal sealed class ComicBrand : Control {
    public ComicBrand(){DoubleBuffered=true;BackColor=Comic.Paper;}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);var state=g.Save();g.TranslateTransform(1,4);g.RotateTransform(-3);Comic.Headline(g,"RAWER",new Rectangle(0,0,Width-4,Height-9),45,Comic.Yellow);g.Restore(state);}
}
internal sealed class ComicFrame : Panel {
    public int Number;public string Caption;public Color Accent;Bitmap backdrop;public static int CacheBuilds;
    public ComicFrame(int number,string caption,Color accent){Number=number;Caption=caption;Accent=accent;DoubleBuffered=true;ResizeRedraw=true;BackColor=Comic.Paper;Padding=new Padding(18,68,18,18);}
    protected override void OnSizeChanged(EventArgs e){if(backdrop!=null){backdrop.Dispose();backdrop=null;}base.OnSizeChanged(e);}
    void Build(){
        backdrop=new Bitmap(Width,Height,PixelFormat.Format32bppRgb);CacheBuilds++;
        using(var g=Graphics.FromImage(backdrop)){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Comic.Paper);
            using(var path=new GraphicsPath()){
                path.AddBezier(4,6,Width*.30f,1,Width*.74f,5,Width-9,3);
                path.AddBezier(Width-9,3,Width-4,Height*.35f,Width-10,Height*.70f,Width-7,Height-10);
                path.AddBezier(Width-7,Height-10,Width*.63f,Height-4,Width*.29f,Height-9,6,Height-7);
                path.AddBezier(6,Height-7,3,Height*.7f,7,Height*.3f,4,6);path.CloseFigure();
                var shifted=g.Save();g.TranslateTransform(4,4);using(var b=new SolidBrush(Comic.Ink))g.FillPath(b,path);g.Restore(shifted);
                using(var b=new SolidBrush(Accent))g.FillPath(b,path);
                var clip=g.Save();g.SetClip(path);Comic.Dots(g,ClientRectangle);
                using(var ray=new Pen(Color.FromArgb(45,Color.White),8))for(int i=0;i<10;i++)g.DrawLine(ray,Width,0,(int)(Width-i*Width/6.0),Height);
                g.Restore(clip);using(var p=new Pen(Comic.Ink,3.2f){LineJoin=LineJoin.Round})g.DrawPath(p,path);
            }
            bool emblem=Number==3||(Number==2&&Height<290);int reserve=emblem?111:28;
            using(var p=Comic.Balloon(new RectangleF(13,13,Math.Max(80,Width-reserve),43),false))using(var fill=new SolidBrush(Comic.White))using(var edge=new Pen(Comic.Ink,2.4f)){g.FillPath(fill,p);g.DrawPath(edge,p);}
            using(var fill=new SolidBrush(Comic.Ink))g.FillEllipse(fill,19,18,32,32);
            using(var f=new Font("Comic Sans MS",14,FontStyle.Bold))Comic.Text(g,Number.ToString("00"),f,Comic.White,new Rectangle(19,16,32,35),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            Comic.Headline(g,Caption,new Rectangle(61,13,Math.Max(50,Width-reserve-63),42),25,Comic.White);
            string key=Number==1?"source":(Number==2?"compression":(Number==3?"folder":"results"));
            if(emblem)ComicArt.Draw(g,key,new Rectangle(Width-108,0,100,67));
        }
    }
    protected override void OnPaintBackground(PaintEventArgs e){if(Width<20||Height<20){e.Graphics.Clear(BackColor);return;}if(backdrop==null)Build();e.Graphics.DrawImageUnscaled(backdrop,0,0);}
    protected override void Dispose(bool disposing){if(disposing&&backdrop!=null)backdrop.Dispose();base.Dispose(disposing);}
}
internal sealed class ComicSpread : Panel {
    public readonly ComicFrame Sources=new ComicFrame(1,"ИСХОДНИКИ",Comic.Mint),Modes=new ComicFrame(2,"ВЫБЕРИ СЖАТИЕ",Comic.Blue),Destination=new ComicFrame(3,"СОХРАНИТЬ",Comic.Yellow),Results=new ComicFrame(4,"ГОТОВЫЕ ФАЙЛЫ",Comic.Purple);
    public ComicSpread(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Comic.Paper;Controls.AddRange(new Control[]{Sources,Modes,Destination,Results});}
    protected override void OnLayout(LayoutEventArgs e){
        if(Width<100||Height<100||(FindForm()!=null&&FindForm().WindowState==FormWindowState.Minimized))return;
        base.OnLayout(e);int gap=36,rowGap=30,left=(int)((ClientSize.Width-gap)*0.55),right=ClientSize.Width-gap-left,top=(ClientSize.Height-rowGap)/2,bottom=ClientSize.Height-rowGap-top;
        Sources.SetBounds(0,0,left,top);Modes.SetBounds(left+gap,0,right,top);Results.SetBounds(0,top+rowGap,left,bottom);Destination.SetBounds(left+gap,top+rowGap,right,bottom);
    }
    static void Arrow(Graphics g,PointF center,bool vertical,bool reverse){
        var state=g.Save();g.TranslateTransform(center.X,center.Y);if(vertical)g.RotateTransform(90);else if(reverse)g.RotateTransform(180);
        var points=new[]{new PointF(-16,-5),new PointF(0,-6),new PointF(-1,-12),new PointF(17,0),new PointF(0,12),new PointF(1,5),new PointF(-16,6)};
        using(var fill=new SolidBrush(Comic.Yellow))using(var edge=new Pen(Comic.Ink,2.5f)){g.FillPolygon(fill,points);g.DrawPolygon(edge,points);}g.Restore(state);
    }
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;Arrow(g,new PointF(Sources.Right+18,Sources.Height/2f),false,false);Arrow(g,new PointF(Modes.Left+Modes.Width/2f,Sources.Bottom+15),true,false);Arrow(g,new PointF(Results.Right+18,Results.Top+Results.Height/2f),false,true);}
}
internal class ComicButton : Button {
    protected bool Hover,Pressed;public Color FillColor=Comic.White;
    public ComicButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Comic.White;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;Cursor=Cursors.Hand;}
    protected override void OnMouseEnter(EventArgs e){Hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){Hover=false;Pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){Pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){Pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space)Pressed=true;Invalidate();base.OnKeyDown(e);}
    protected override void OnKeyUp(KeyEventArgs e){Pressed=false;Invalidate();base.OnKeyUp(e);}
    protected override void OnPaint(PaintEventArgs e){
        if(Width<10||Height<10)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;int shift=Pressed&&Enabled?2:0;
        var fill=Enabled?(Hover?ControlPaint.Light(FillColor,0.15f):FillColor):Color.FromArgb(232,229,226);
        Comic.Box(g,new Rectangle(1+shift,1+shift,Width-7,Height-7),fill,Pressed?1:3);
        Comic.Text(g,Text,Font,Enabled?Comic.Ink:Color.FromArgb(144,137,141),new Rectangle(8+shift,shift,Width-21,Height-6),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(5,5,Width-14,Height-14));
    }
}
internal sealed class ComicRatioButton : ComicButton {
    bool selected;public readonly int Ratio;static readonly Font ratioFont=new Font("Impact",43,FontStyle.Italic),smallFont=new Font("Comic Sans MS",8.5f,FontStyle.Bold);
    public bool Selected {get{return selected;}set{if(selected==value)return;selected=value;Invalidate();}}
    public ComicRatioButton(int ratio){Ratio=ratio;AccessibleRole=AccessibleRole.RadioButton;AccessibleName="Сжатие в "+ratio+" раза";Text=ratio+"×";}
    protected override void OnPaint(PaintEventArgs e){
        if(Width<10||Height<10)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Comic.White);int shift=Pressed&&Enabled?2:0;
        Color fill=Selected?(Ratio==2?Comic.Yellow:Comic.Orange):Color.FromArgb(230,226,223);if(Hover&&Enabled)fill=ControlPaint.Light(fill,0.12f);if(!Enabled)fill=ControlPaint.Light(fill,0.2f);
        using(var p=Comic.Balloon(new RectangleF(3+shift,4+shift,Width-12,Height-14),true)){
            var state=g.Save();g.TranslateTransform(3,3);using(var b=new SolidBrush(Comic.Ink))g.FillPath(b,p);g.Restore(state);
            using(var b=new SolidBrush(fill))g.FillPath(b,p);
            if(Selected){state=g.Save();g.SetClip(p);Comic.Dots(g,ClientRectangle);g.Restore(state);}
            using(var edge=new Pen(Comic.Ink,2.8f))g.DrawPath(edge,p);
        }
        Comic.Text(g,Ratio+"×",ratioFont,Selected?Comic.Ink:Color.FromArgb(134,127,132),new Rectangle(12+shift,4+shift,Width-30,Height-47),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        Comic.Text(g,Selected?"✓ ВЫБРАНО":(Ratio==2?"БЕРЕЖНЕЕ":"МЕНЬШЕ РАЗМЕР"),smallFont,Selected?Comic.Ink:Comic.Muted,new Rectangle(10+shift,Height-43+shift,Width-29,20),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(9,9,Width-23,Height-28));
    }
}
internal sealed class ComicGrid : DataGridView {public ComicGrid(){DoubleBuffered=true;}}
internal sealed class ComicEmpty : Panel {
    public bool IsResults;Bitmap cached;
    public ComicEmpty(bool results){IsResults=results;DoubleBuffered=true;ResizeRedraw=true;BackColor=Comic.White;SetStyle(ControlStyles.Opaque,true);}
    protected override void OnSizeChanged(EventArgs e){if(cached!=null){cached.Dispose();cached=null;}base.OnSizeChanged(e);}
    protected override void OnPaintBackground(PaintEventArgs e){ }
    protected override void OnPaint(PaintEventArgs e){
        if(Width<20||Height<20)return;
        if(cached==null){cached=new Bitmap(Width,Height,PixelFormat.Format32bppRgb);using(var g=Graphics.FromImage(cached)){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(IsResults?Color.FromArgb(221,139,236):Color.FromArgb(96,218,105));Comic.Dots(g,ClientRectangle);
            int art=Math.Min(215,(int)(Width*0.43));ComicArt.Draw(g,IsResults?"results":"source",new Rectangle(-5,1,art,Height-2));
            int x=art-2,w=Math.Max(80,Width-x-10),h=Math.Min(125,Height-10),y=(Height-h)/2;
            using(var p=Comic.Balloon(new RectangleF(x,y,w,h),true))using(var b=new SolidBrush(Comic.White))using(var edge=new Pen(Comic.Ink,2.5f)){g.FillPath(b,p);g.DrawPath(edge,p);}
            g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})using(var ink=new SolidBrush(Comic.Ink)){
                using(var f=new Font("Impact",IsResults?18:17,FontStyle.Italic))g.DrawString(IsResults?"ГОТОВО!":"ТВОЁ КИНО!",f,ink,new RectangleF(x+12,y+8,w-24,31),format);
                using(var f=new Font("Comic Sans MS",9.5f))g.DrawString(IsResults?"Здесь появятся\nновые копии R3D":"Перетащи видео\nили выбери папку",f,ink,new RectangleF(x+14,y+42,w-28,Math.Max(20,h-58)),format);
            }
        }}
        e.Graphics.DrawImageUnscaled(cached,0,0);
    }
    protected override void Dispose(bool disposing){if(disposing&&cached!=null)cached.Dispose();base.Dispose(disposing);}
}
internal sealed class ComicMeter : Control {
    int value,maximum=1000,offset;ProgressBarStyle style;readonly Timer timer=new Timer();public Color FillColor=Comic.Yellow;
    public ComicMeter(){DoubleBuffered=true;BackColor=Comic.White;timer.Interval=60;timer.Tick+=(s,e)=>{if(FindForm()!=null&&FindForm().WindowState==FormWindowState.Minimized)return;offset=(offset+6)%Math.Max(1,Width+Width/4);Invalidate();};}
    public int Value {get{return value;}set{int next=Math.Max(0,Math.Min(maximum,value));if(this.value==next)return;this.value=next;Invalidate();}}
    public int Maximum {get{return maximum;}set{maximum=Math.Max(1,value);Invalidate();}}
    public ProgressBarStyle Style {get{return style;}set{style=value;if(style==ProgressBarStyle.Marquee)timer.Start();else timer.Stop();Invalidate();}}
    protected override void OnPaint(PaintEventArgs e){
        if(Width<6||Height<6)return;var g=e.Graphics;using(var fill=new SolidBrush(Color.FromArgb(238,234,224)))g.FillRectangle(fill,1,1,Width-3,Height-3);
        int w=style==ProgressBarStyle.Marquee?Width/4:(int)((Width-4)*(double)value/maximum),x=style==ProgressBarStyle.Marquee?offset-Width/4:2;
        var state=g.Save();g.SetClip(new Rectangle(2,2,Width-5,Height-5));using(var fill=new SolidBrush(FillColor))g.FillRectangle(fill,x,2,w,Height-4);using(var pen=new Pen(Color.FromArgb(85,Comic.Ink)))for(int i=x;i<x+w;i+=10)g.DrawLine(pen,i,Height-3,i+Height,2);g.Restore(state);
        using(var pen=new Pen(Comic.Ink,2))g.DrawRectangle(pen,1,1,Width-3,Height-3);
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
}
