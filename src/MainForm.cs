using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Rawer {
public sealed partial class MainForm : Form {
    static readonly Color Ink=Comic.Ink,Muted=Comic.Muted,Blue=Color.FromArgb(28,105,147);
    readonly List<ClipJob> jobs=new List<ClipJob>();readonly Dictionary<Guid,DataGridViewRow> rows=new Dictionary<Guid,DataGridViewRow>();
    readonly List<CompletedClip> outputs=new List<CompletedClip>();readonly HashSet<Guid> shownOutputs=new HashSet<Guid>();
    readonly Button addFiles=ButtonOf("＋ Файлы"),addFolder=ButtonOf("＋ Папка"),remove=ButtonOf("Убрать"),clear=ButtonOf("Очистить");
    readonly Button chooseFolder=ButtonOf("Выбрать папку…"),start=ButtonOf("ПОЕХАЛИ!  →",true),stop=ButtonOf("Стоп"),openFolder=ButtonOf("Открыть папку"),showFile=ButtonOf("Показать файл");
    readonly ComicRatioButton mode2=new ComicRatioButton(2),mode4=new ComicRatioButton(4);
    readonly Label countLabel=LabelOf("Очередь пуста",9,false),spaceLabel=LabelOf("",8.5f,false),stageLabel=LabelOf("Готов к работе",10,true),timingLabel=LabelOf("",9,false),progressLabel=LabelOf("Добавьте исходники, выберите режим и папку",8.5f,false),savedLabel=LabelOf("Готовых копий пока нет",9,true),footer=LabelOf("NIKON ZR  /  6K  /  RAW  /  ИСХОДНЫЙ ЗВУК",8.5f,true);
    readonly Label modeNote=LabelOf("",9.5f,false),estimateLabel=LabelOf("",10,true),namingLabel=LabelOf("",8.5f,false);
    readonly TextBox destination=new TextBox();readonly ComicMeter progress=new ComicMeter();readonly ComicGrid grid=new ComicGrid(),resultGrid=new ComicGrid();readonly Panel empty=new ComicEmpty(false),resultEmpty=new ComicEmpty(true);
    TableLayoutPanel sourceView,finishedView;ComicIllustration sourceDrawing,finishedDrawing;
    readonly ToolTip tips=new ToolTip{AutoPopDelay=20000,InitialDelay=350,ReshowDelay=100,ShowAlways=true,OwnerDraw=true};
    readonly Font hintFont=new Font("Segoe UI",9.5f);
    readonly Font modeCellFont=new Font("Segoe UI",9.5f,FontStyle.Bold);
    readonly string settingsPath;readonly bool preview;readonly AppSettings settings;CancellationTokenSource cancellation,loadCancellation;bool busy,loading,closeAfterStop,destinationExists;int targetRatio=2;BatchProgress pendingProgress;
    readonly System.Windows.Forms.Timer clockTimer=new System.Windows.Forms.Timer();DateTime runStarted,lastClock;
    public MainForm():this(AppSettings.DefaultPath,false){}
    internal MainForm(string config,bool previewMode){
        settingsPath=config;preview=previewMode;settings=preview?new AppSettings():AppSettings.Load(settingsPath);targetRatio=settings.TargetRatio==4?4:2;
        Text="RAWER 0.6.2 — 2× / 4×";Font=new Font("Segoe UI",10);ForeColor=Ink;BackColor=Comic.Paper;AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        DoubleBuffered=true;
        ClientSize=new Size(1220,820);MinimumSize=new Size(1000,730);StartPosition=FormStartPosition.CenterScreen;AllowDrop=true;
        try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
        Load+=(s,e)=>{if(preview)return;var area=Screen.FromControl(this).WorkingArea;int maxWidth=Math.Max(600,area.Width-24),maxHeight=Math.Max(500,area.Height-24);MinimumSize=new Size(Math.Min(MinimumSize.Width,maxWidth),Math.Min(MinimumSize.Height,maxHeight));Size=new Size(Math.Min(Width,maxWidth),Math.Min(Height,maxHeight));Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);};
        var shell=new ComicPaper{Dock=DockStyle.Fill,AutoScrollMinSize=new Size(980,700)};Controls.Add(shell);
        var layout=new ComicPageLayout{Dock=DockStyle.Top,Height=820,Padding=new Padding(22,12,22,8),ColumnCount=1,RowCount=3,BackColor=Comic.Paper};shell.Controls.Add(layout);
        shell.ClientSizeChanged+=(s,e)=>{if(WindowState==FormWindowState.Minimized||shell.ClientSize.Height<100)return;int h=Math.Max(700,shell.ClientSize.Height);if(layout.Height!=h)layout.Height=h;};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=Padding.Empty,BackColor=Comic.Paper};header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,172));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,116));
        var logo=new ComicBrand{Dock=DockStyle.Fill};header.Controls.Add(logo,0,0);
        var about=ButtonOf("О RAWER");about.Dock=DockStyle.Top;about.Height=34;about.Margin=new Padding(0,10,0,0);about.Click+=(s,e)=>ShowAbout();header.Controls.Add(about,2,0);layout.Controls.Add(header,0,0);
        var spread=new ComicSpread{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,8)};layout.Controls.Add(spread,0,1);
        BuildSources(spread.Sources);BuildModes(spread.Modes);BuildDestination(spread.Destination);BuildResults(spread.Results);
        tips.Popup+=(s,e)=>{string text=tips.GetToolTip(e.AssociatedControl);Size size=TextRenderer.MeasureText(text,hintFont,new Size(430,10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPadding);e.ToolTipSize=new Size(size.Width+26,size.Height+26);};
        tips.Draw+=(s,e)=>{e.Graphics.Clear(Comic.White);using(var pen=new Pen(Ink,2))e.Graphics.DrawRectangle(pen,1,1,e.Bounds.Width-3,e.Bounds.Height-3);TextRenderer.DrawText(e.Graphics,e.ToolTipText,hintFont,new Rectangle(13,13,e.Bounds.Width-26,e.Bounds.Height-26),Ink,TextFormatFlags.WordBreak|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);};
        footer.ForeColor=Muted;footer.BackColor=Comic.Paper;footer.Dock=DockStyle.Fill;footer.TextAlign=ContentAlignment.MiddleLeft;layout.Controls.Add(footer,0,2);
        tips.SetToolTip(mode2,"Сжатие примерно в 2 раза: целевой размер — около 50% оригинала.\nБолее бережное повторное сжатие RAW. Разрешение 6K, RAW-настройки, исходный звук и таймкод сохраняются.\nСжатие с потерями. Итоговый размер и качество зависят от материала. Для новых сцен сравните результат в DaVinci Resolve.");
        tips.SetToolTip(mode4,"Сжатие примерно в 4 раза: целевой размер — около 25% оригинала.\nСильнее сжимает RAW; потери деталей и изменения шума могут стать заметнее, особенно при сильной обработке.\nРазрешение 6K, RAW-метаданные, исходный звук и таймкод сохраняются. Для новых сцен сравните изображение в DaVinci Resolve. Оригинал остаётся на месте.");
        tips.SetToolTip(addFolder,"Добавить R3D из выбранной папки и всех вложенных папок. Готовые копии с отчётом RAWER пропускаются.");
        tips.SetToolTip(clear,"Очистить список исходников. Видео и готовые копии на диске останутся.");
        tips.SetToolTip(showFile,"Показать выбранный готовый файл в Проводнике. Можно также дважды щёлкнуть по строке.");
        mode2.Click+=(s,e)=>SelectRatio(2);mode4.Click+=(s,e)=>SelectRatio(4);
        addFiles.Click+=async(s,e)=>await ChooseFiles();addFolder.Click+=async(s,e)=>await ChooseInputFolder();remove.Click+=(s,e)=>RemoveSelected();clear.Click+=(s,e)=>ClearQueue();chooseFolder.Click+=(s,e)=>ChooseDestination();start.Click+=async(s,e)=>await RunBatch();stop.Click+=(s,e)=>StopBatch();openFolder.Click+=(s,e)=>OpenDestination();showFile.Click+=(s,e)=>OpenSelectedResult();destination.TextChanged+=(s,e)=>RefreshButtons();destination.Leave+=(s,e)=>{RememberFolder();RefreshSpace();};
        grid.SelectionChanged+=(s,e)=>RefreshButtons();grid.CellDoubleClick+=(s,e)=>ShowSourceInfo();grid.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Delete&&!busy&&!loading){RemoveSelected();e.Handled=true;}};
        resultGrid.SelectionChanged+=(s,e)=>RefreshButtons();resultGrid.CellDoubleClick+=(s,e)=>OpenSelectedResult();
        EnableDrop(this);EnableDrop(grid);EnableDrop(empty);EnableDrop(spread.Sources);
        FormClosing+=(s,e)=>{if(busy||loading){e.Cancel=true;closeAfterStop=true;StopBatch();}};
        FormClosed+=(s,e)=>{clockTimer.Dispose();tips.Dispose();hintFont.Dispose();modeCellFont.Dispose();};clockTimer.Interval=100;clockTimer.Tick+=(s,e)=>{
            if(!busy||WindowState==FormWindowState.Minimized)return;var latest=Interlocked.Exchange(ref pendingProgress,null);if(latest!=null)OnProgress(latest);
            var now=DateTime.UtcNow;if((now-lastClock).TotalSeconds>=1){timingLabel.Text="Прошло "+UserText.Time((now-runStarted).TotalSeconds);lastClock=now;}
        };
        string saved=settings.DestinationFolder;if(string.IsNullOrWhiteSpace(saved)){string videos=Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);if(string.IsNullOrWhiteSpace(videos))videos=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);saved=Path.Combine(videos,"RAWER");}destination.Text=preview?Path.Combine(Path.GetPathRoot(Environment.SystemDirectory),"Videos","Compressed"):saved;
        UpdateMode();RefreshQueue();RefreshResults();RefreshSpace();
    }
    void EnableDrop(Control control){
        control.AllowDrop=true;control.DragEnter+=(s,e)=>{if(!busy&&!loading&&e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};control.DragDrop+=async(s,e)=>{if(!busy&&!loading&&e.Data.GetDataPresent(DataFormats.FileDrop))await AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));};
    }
    void BuildSources(Panel panel){
        var body=Table(1,3);body.RowStyles.Add(new RowStyle(SizeType.Absolute,38));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,24));panel.Controls.Add(body);
        var buttons=Flow();buttons.Controls.Add(addFiles);buttons.Controls.Add(addFolder);buttons.Controls.Add(remove);buttons.Controls.Add(clear);body.Controls.Add(buttons,0,0);
        sourceView=Table(2,1);sourceView.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));sourceView.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,0));body.Controls.Add(sourceView,0,1);
        var queue=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Comic.White};sourceView.Controls.Add(queue,0,0);BuildGrid(grid,false);queue.Controls.Add(grid);empty.Dock=DockStyle.Fill;queue.Controls.Add(empty);
        sourceDrawing=new ComicIllustration("source"){Dock=DockStyle.Fill,Margin=Padding.Empty,Visible=false};sourceView.Controls.Add(sourceDrawing,1,0);sourceView.ClientSizeChanged+=(s,e)=>LayoutSideArt(sourceView,sourceDrawing,jobs.Count>0);
        countLabel.Dock=DockStyle.Fill;countLabel.TextAlign=ContentAlignment.MiddleLeft;countLabel.ForeColor=Muted;body.Controls.Add(countLabel,0,2);
    }
    void BuildModes(Panel panel){
        var body=Table(1,4);body.RowStyles.Add(new RowStyle(SizeType.Absolute,116));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,27));body.RowStyles.Add(new RowStyle(SizeType.Absolute,21));panel.Controls.Add(body);
        var choices=Table(2,1);choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));mode2.Dock=mode4.Dock=DockStyle.Fill;mode2.Margin=new Padding(0,0,10,0);mode4.Margin=new Padding(4,0,0,0);choices.Controls.Add(mode2,0,0);choices.Controls.Add(mode4,1,0);body.Controls.Add(choices,0,0);
        var story=Table(2,1);story.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,116));story.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));var drawing=new ComicIllustration("compression"){Dock=DockStyle.Fill,Margin=new Padding(0,2,7,0)};story.Controls.Add(drawing,0,0);
        story.ClientSizeChanged+=(s,e)=>{bool show=story.Height>=64;float w=show?116:0;if(story.ColumnStyles[0].Width!=w)story.ColumnStyles[0].Width=w;if(drawing.Visible!=show)drawing.Visible=show;};
        modeNote.Dock=DockStyle.Fill;modeNote.AutoSize=false;modeNote.Font=new Font("Comic Sans MS",9);modeNote.TextAlign=ContentAlignment.MiddleLeft;modeNote.Padding=Padding.Empty;story.Controls.Add(modeNote,1,0);body.Controls.Add(story,0,1);
        estimateLabel.Dock=DockStyle.Fill;estimateLabel.AutoSize=false;estimateLabel.AutoEllipsis=true;estimateLabel.TextAlign=ContentAlignment.MiddleLeft;body.Controls.Add(estimateLabel,0,2);
        var hint=LabelOf("6K + RAW + ЗВУК  /  наведи на кнопку для подробностей",8,false);hint.ForeColor=Muted;hint.Dock=DockStyle.Fill;hint.TextAlign=ContentAlignment.MiddleLeft;body.Controls.Add(hint,0,3);
    }
    void BuildDestination(Panel panel){
        var body=Table(1,7);foreach(int h in new[]{28,34,22,43,23,18})body.RowStyles.Add(new RowStyle(SizeType.Absolute,h));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));panel.Controls.Add(body);
        destination.Dock=DockStyle.Fill;destination.Margin=new Padding(1,1,4,0);destination.Font=new Font("Segoe UI",10);destination.ForeColor=Ink;destination.BorderStyle=BorderStyle.FixedSingle;destination.AccessibleName="Папка сохранения";body.Controls.Add(destination,0,0);
        var folder=Table(2,1);folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));chooseFolder.Dock=DockStyle.Fill;chooseFolder.AutoSize=false;chooseFolder.Margin=new Padding(0,0,6,0);folder.Controls.Add(chooseFolder,0,0);spaceLabel.AutoSize=false;spaceLabel.Dock=DockStyle.Fill;spaceLabel.ForeColor=Muted;spaceLabel.TextAlign=ContentAlignment.MiddleRight;folder.Controls.Add(spaceLabel,1,0);body.Controls.Add(folder,0,1);
        namingLabel.Dock=DockStyle.Fill;namingLabel.AutoSize=false;namingLabel.AutoEllipsis=true;namingLabel.ForeColor=Muted;namingLabel.TextAlign=ContentAlignment.MiddleLeft;body.Controls.Add(namingLabel,0,2);
        var run=Table(2,1);run.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));run.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,78));start.Dock=DockStyle.Fill;start.AutoSize=false;start.Font=new Font("Impact",15);start.Margin=new Padding(0,0,8,0);stop.Dock=DockStyle.Fill;stop.AutoSize=false;stop.Margin=Padding.Empty;run.Controls.Add(start,0,0);run.Controls.Add(stop,1,0);body.Controls.Add(run,0,3);
        stageLabel.Dock=DockStyle.Fill;stageLabel.AutoSize=false;stageLabel.AutoEllipsis=true;stageLabel.TextAlign=ContentAlignment.MiddleLeft;body.Controls.Add(stageLabel,0,4);
        progress.Maximum=1000;progress.Dock=DockStyle.Fill;progress.Margin=new Padding(0,1,4,1);progress.AccessibleName="Прогресс сжатия";body.Controls.Add(progress,0,5);
        progressLabel.Dock=DockStyle.Fill;progressLabel.AutoSize=false;progressLabel.AutoEllipsis=true;progressLabel.ForeColor=Muted;progressLabel.TextAlign=ContentAlignment.TopLeft;body.Controls.Add(progressLabel,0,6);
    }
    void BuildResults(Panel panel){
        var body=Table(1,3);body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,28));body.RowStyles.Add(new RowStyle(SizeType.Absolute,36));panel.Controls.Add(body);
        finishedView=Table(2,1);finishedView.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));finishedView.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,0));body.Controls.Add(finishedView,0,0);
        var files=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Comic.White};finishedView.Controls.Add(files,0,0);BuildGrid(resultGrid,true);files.Controls.Add(resultGrid);resultEmpty.Dock=DockStyle.Fill;files.Controls.Add(resultEmpty);
        finishedDrawing=new ComicIllustration("results"){Dock=DockStyle.Fill,Margin=Padding.Empty,Visible=false};finishedView.Controls.Add(finishedDrawing,1,0);finishedView.ClientSizeChanged+=(s,e)=>LayoutSideArt(finishedView,finishedDrawing,outputs.Count>0);
        var stats=Table(2,1);stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));savedLabel.Dock=DockStyle.Fill;savedLabel.AutoSize=false;savedLabel.AutoEllipsis=true;savedLabel.TextAlign=ContentAlignment.MiddleLeft;stats.Controls.Add(savedLabel,0,0);timingLabel.Dock=DockStyle.Fill;timingLabel.AutoSize=false;timingLabel.ForeColor=Muted;timingLabel.TextAlign=ContentAlignment.MiddleRight;stats.Controls.Add(timingLabel,1,0);body.Controls.Add(stats,0,1);
        var actions=Flow();actions.Controls.Add(showFile);actions.Controls.Add(openFolder);body.Controls.Add(actions,0,2);
    }
    static Label LabelOf(string text,float size,bool bold){return new Label{Text=text,AutoSize=true,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=Ink,BackColor=Comic.White,Margin=Padding.Empty};}
    static Button ButtonOf(string text,bool primary=false){return new ComicButton{Text=text,FillColor=primary?Comic.Yellow:Comic.White,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(9,2,9,2),Font=new Font("Comic Sans MS",9,FontStyle.Bold),Margin=new Padding(0,0,5,0),MinimumSize=new Size(62,33)};}
    static TableLayoutPanel Table(int columns,int rows){return new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=columns,RowCount=rows,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Comic.White};}
    static FlowLayoutPanel Flow(){return new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=false,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Comic.White};}
    static void LayoutSideArt(TableLayoutPanel view,Control drawing,bool show){int width=show?Math.Max(0,Math.Min(132,view.ClientSize.Width-405)):0;if(view.ColumnStyles[1].Width!=width)view.ColumnStyles[1].Width=width;if(drawing.Visible!=(width>0))drawing.Visible=width>0;}
    void BuildGrid(ComicGrid view,bool results){
        view.Dock=DockStyle.Fill;view.BackgroundColor=Comic.White;view.BorderStyle=BorderStyle.None;view.ReadOnly=true;view.AllowUserToAddRows=false;view.AllowUserToDeleteRows=false;view.AllowUserToResizeRows=false;view.RowHeadersVisible=false;view.MultiSelect=!results;view.SelectionMode=DataGridViewSelectionMode.FullRowSelect;view.AutoGenerateColumns=false;view.EnableHeadersVisualStyles=false;view.ColumnHeadersHeight=28;view.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;view.RowTemplate.Height=40;view.GridColor=Color.FromArgb(223,218,203);view.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;view.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;view.ScrollBars=ScrollBars.Both;
        view.DefaultCellStyle=new DataGridViewCellStyle{BackColor=Comic.White,ForeColor=Ink,SelectionBackColor=Comic.Yellow,SelectionForeColor=Ink,Font=new Font("Segoe UI",9.5f),Padding=new Padding(6,0,4,0)};view.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(245,242,231);view.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Color.FromArgb(234,230,218),ForeColor=Muted,Font=new Font("Segoe UI",8.5f,FontStyle.Bold),Padding=new Padding(6,0,4,0),SelectionBackColor=Color.FromArgb(234,230,218),SelectionForeColor=Muted};
        view.Columns.Add(new DataGridViewTextBoxColumn{Name="clip",HeaderText=results?"Готовый R3D":"Клип",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,MinimumWidth=140,SortMode=DataGridViewColumnSortMode.NotSortable});
        if(results){
            view.Columns.Add(new DataGridViewTextBoxColumn{Name="mode",HeaderText="Режим",Width=76,SortMode=DataGridViewColumnSortMode.NotSortable});
            view.Columns.Add(new DataGridViewTextBoxColumn{Name="size",HeaderText="Размер",Width=90,SortMode=DataGridViewColumnSortMode.NotSortable});
            view.Columns.Add(new DataGridViewTextBoxColumn{Name="saving",HeaderText="Экономия",Width=88,SortMode=DataGridViewColumnSortMode.NotSortable});
        }else{
            view.Columns.Add(new DataGridViewTextBoxColumn{Name="size",HeaderText="Размер",Width=92,SortMode=DataGridViewColumnSortMode.NotSortable});
            view.Columns.Add(new DataGridViewTextBoxColumn{Name="state",HeaderText="Состояние",Width=145,SortMode=DataGridViewColumnSortMode.NotSortable});
        }
        var menu=new ContextMenuStrip();if(results){menu.Items.Add("Показать файл в Проводнике",null,(s,e)=>OpenSelectedResult());menu.Items.Add("Открыть папку",null,(s,e)=>OpenDestination());}else menu.Items.Add("Убрать из очереди",null,(s,e)=>RemoveSelected());view.ContextMenuStrip=menu;
    }
}
}
