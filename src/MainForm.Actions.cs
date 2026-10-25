using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Rawer {
public sealed partial class MainForm {
    internal Action<double> ProgressDiagnostic {get;set;}
    async Task ChooseFiles(){
        if(busy||loading)return;
        using(var dialog=new OpenFileDialog{Filter="Nikon R3D-NE (*.R3D)|*.R3D",Multiselect=true,Title="Добавить клипы Nikon ZR",CheckFileExists=true}){
            if(Directory.Exists(settings.LastInputFolder))dialog.InitialDirectory=settings.LastInputFolder;
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;settings.LastInputFolder=Path.GetDirectoryName(dialog.FileName);await AddPaths(dialog.FileNames);
        }
    }
    async Task ChooseInputFolder(){if(busy||loading)return;using(var dialog=new FolderBrowserDialog{Description="Добавить R3D из папки и вложенных папок",ShowNewFolderButton=false,SelectedPath=Directory.Exists(settings.LastInputFolder)?settings.LastInputFolder:""})if(dialog.ShowDialog(this)==DialogResult.OK){settings.LastInputFolder=dialog.SelectedPath;await AddPaths(new[]{dialog.SelectedPath});}}
    public async Task AddPaths(IEnumerable<string> inputs){
        if(busy||loading)return;loading=true;closeAfterStop=false;loadCancellation=new CancellationTokenSource();stageLabel.Text="Читаем клипы…";progress.Style=ProgressBarStyle.Marquee;RefreshButtons();
        string folder=destination.Text;var existing=new HashSet<string>(jobs.Select(j=>j.SourcePath),StringComparer.OrdinalIgnoreCase);string[] originals=inputs.ToArray();
        try{
            var token=loadCancellation.Token;var added=await Task.Run(()=>InputFiles.Collect(originals,folder,token).Where(p=>!existing.Contains(p)).Select(p=>{token.ThrowIfCancellationRequested();return ClipJob.Inspect(p);}).ToArray());
            grid.SuspendLayout();try{foreach(var job in added){job.SelectMode(targetRatio);jobs.Add(job);AddRow(job);}}finally{grid.ResumeLayout();}
            grid.ClearSelection();stageLabel.Text=added.Length==0?"Новых R3D не найдено":"Готов к сжатию "+targetRatio+"×";
            progressLabel.Text=added.Any(j=>j.Status==JobStatus.Unsupported)?"Есть неподдерживаемые клипы. Наведите на строку для подробностей.":"Выберите папку и нажмите «Сжать».";
        }catch(OperationCanceledException){stageLabel.Text="Добавление остановлено";progressLabel.Text="Ранее добавленные клипы остались в очереди.";}
        catch(Exception e){stageLabel.Text="Не удалось добавить файлы";progressLabel.Text=UserText.Error(e);}
        finally{loading=false;loadCancellation.Dispose();loadCancellation=null;progress.Style=ProgressBarStyle.Continuous;progress.Value=0;RefreshQueue();if(closeAfterStop)Close();}
    }
    void AddRow(ClipJob job){int i=grid.Rows.Add(job.Name,UserText.Size(job.SourceBytes),UserText.Status(job.Status));var row=grid.Rows[i];row.Tag=job.Id;rows.Add(job.Id,row);UpdateRow(job,null);}
    void UpdateRow(ClipJob job,string live){
        DataGridViewRow row;if(!rows.TryGetValue(job.Id,out row))return;
        row.Cells[0].Value=job.Name;row.Cells[1].Value=UserText.Size(job.SourceBytes);row.Cells[2].Value=live??(UserText.Status(job.Status)+(job.Status==JobStatus.Completed?" · "+job.TargetRatio+"×":""));
        row.Cells[2].Style.ForeColor=job.Status==JobStatus.Completed?Color.FromArgb(29,113,77):(job.Status==JobStatus.Failed||job.Status==JobStatus.Unsupported?Color.FromArgb(164,62,35):(job.Status==JobStatus.Running||job.Status==JobStatus.Verifying?Blue:Muted));
        string tip=job.SourcePath;if(job.Frames>0)tip+="\n6K · "+job.Frames+" кадров · "+job.Duration.ToString("0.00")+" с";if(!string.IsNullOrEmpty(job.Error))tip+="\n"+job.Error;
        foreach(DataGridViewCell cell in row.Cells)cell.ToolTipText=tip;
    }
    void RefreshQueue(){
        bool has=jobs.Count>0;grid.Visible=has;empty.Visible=!has;if(has)grid.BringToFront();else empty.BringToFront();
        LayoutSideArt(sourceView,sourceDrawing,has);
        countLabel.Text=has?UserText.Clips(jobs.Count)+"  ·  "+UserText.Size(jobs.Sum(j=>j.SourceBytes)):"Очередь пуста";UpdateMode();RefreshButtons();
    }
    void RefreshResults(){
        foreach(var job in jobs.Where(j=>j.Status==JobStatus.Completed))foreach(var output in job.Outputs)if(shownOutputs.Add(output.Id)){
            outputs.Add(output);int index=resultGrid.Rows.Add(output.Name,output.TargetRatio+"×",UserText.Size(output.OutputBytes),(100.0*(1-(double)output.OutputBytes/Math.Max(1,output.SourceBytes))).ToString("0.0")+"%");
            var row=resultGrid.Rows[index];row.Tag=output.Id;row.Cells[1].Style.Font=modeCellFont;
            foreach(DataGridViewCell cell in row.Cells)cell.ToolTipText=output.OutputPath+"\n"+output.TargetRatio+"× · оригинал "+UserText.Size(output.SourceBytes)+" → "+UserText.Size(output.OutputBytes);
        }
        bool has=outputs.Count>0;resultGrid.Visible=has;resultEmpty.Visible=!has;if(has)resultGrid.BringToFront();else resultEmpty.BringToFront();
        LayoutSideArt(finishedView,finishedDrawing,has);
        if(!busy)savedLabel.Text=has?"Копий: "+outputs.Count+"  ·  "+UserText.Size(outputs.Sum(o=>o.OutputBytes)):"Готовых копий пока нет";RefreshButtons();
    }
    void SelectRatio(int ratio){
        if(busy||loading)return;targetRatio=CompressionMode.Require(ratio);
        foreach(var job in jobs){job.SelectMode(targetRatio);UpdateRow(job,null);}progress.Value=0;UpdateMode();RefreshQueue();RememberFolder();
        stageLabel.Text="Выбран режим "+targetRatio+"×";progressLabel.Text="Готов к сжатию "+targetRatio+"×. Оригиналы останутся на месте.";
    }
    void UpdateMode(){
        mode2.Selected=targetRatio==2;mode4.Selected=targetRatio==4;
        modeNote.Text=targetRatio==2?"Бережнее к деталям.\nУмеренное сжатие RAW.":"Больше экономии места.\nСильнее влияет на детали и шум.";
        long bytes=jobs.Where(j=>j.Status!=JobStatus.Unsupported).Sum(j=>j.SourceBytes);
        estimateLabel.Text=bytes>0?UserText.Size(bytes)+" → ≈ "+UserText.Size(bytes/targetRatio):(targetRatio==2?"Около 50% исходного размера":"Около 25% исходного размера");
        namingLabel.Text="Копии: имя_RAWER_"+targetRatio+"x.R3D · оригиналы целы";
        progress.FillColor=targetRatio==2?Comic.Yellow:Comic.Orange;progress.Invalidate();((ComicButton)start).FillColor=targetRatio==2?Comic.Yellow:Comic.Orange;start.Invalidate();
    }
    void RefreshButtons(){
        bool edit=!busy&&!loading;addFiles.Enabled=addFolder.Enabled=chooseFolder.Enabled=destination.Enabled=mode2.Enabled=mode4.Enabled=edit;remove.Enabled=edit&&grid.SelectedRows.Count>0;clear.Enabled=edit&&jobs.Count>0;
        int count=jobs.Count(j=>j.CanRun);start.Enabled=edit&&count>0&&!string.IsNullOrWhiteSpace(destination.Text);start.Text=count>0?"СЖАТЬ "+UserText.Clips(count).ToUpperInvariant()+" · "+targetRatio+"×  →":"ПОЕХАЛИ!  →";
        stop.Enabled=(busy&&cancellation!=null&&!cancellation.IsCancellationRequested)||(loading&&loadCancellation!=null&&!loadCancellation.IsCancellationRequested);
        showFile.Enabled=resultGrid.SelectedRows.Count==1;openFolder.Enabled=outputs.Count>0||destinationExists;
    }
    void RemoveSelected(){if(busy||loading)return;var ids=new HashSet<Guid>(grid.SelectedRows.Cast<DataGridViewRow>().Select(r=>(Guid)r.Tag));foreach(var job in jobs.Where(j=>ids.Contains(j.Id)).ToArray()){grid.Rows.Remove(rows[job.Id]);rows.Remove(job.Id);jobs.Remove(job);}RefreshQueue();}
    void ClearQueue(){if(busy||loading)return;jobs.Clear();rows.Clear();grid.Rows.Clear();progress.Value=0;stageLabel.Text="Готов к работе";progressLabel.Text="Добавьте исходники, выберите режим и папку.";RefreshQueue();RefreshResults();}
    void ChooseDestination(){if(busy||loading)return;using(var dialog=new FolderBrowserDialog{Description="Куда сохранить сжатые R3D",ShowNewFolderButton=true,SelectedPath=Directory.Exists(destination.Text)?destination.Text:""})if(dialog.ShowDialog(this)==DialogResult.OK){destination.Text=dialog.SelectedPath;RememberFolder();RefreshSpace();}}
    void RememberFolder(){if(preview)return;try{settings.DestinationFolder=destination.Text;settings.TargetRatio=targetRatio;settings.Save(settingsPath);}catch{footer.Text="Папка выбрана, но запомнить настройки не удалось.";}}
    void RefreshSpace(){if(preview){spaceLabel.Text="Демонстрация";destinationExists=false;RefreshButtons();return;}spaceLabel.Text="";destinationExists=false;try{destinationExists=Directory.Exists(destination.Text);string root=Path.GetPathRoot(Path.GetFullPath(destination.Text));var drive=new DriveInfo(root);if(drive.IsReady)spaceLabel.Text="Свободно "+UserText.Size(drive.AvailableFreeSpace);}catch{}RefreshButtons();}
    bool PrepareDestination(out string folder){
        folder=null;try{
            if(!Path.IsPathRooted(destination.Text))throw new IOException("Выберите полный путь к папке сохранения.");folder=Path.GetFullPath(destination.Text);
            string root=Path.GetPathRoot(folder);try{var drive=new DriveInfo(root);long need=(long)(jobs.Where(j=>j.CanRun).Sum(j=>(double)j.SourceBytes)/targetRatio)+32*1024*1024;if(drive.IsReady&&drive.AvailableFreeSpace<need)throw new InvalidOperationException("Нужно примерно "+UserText.Size(need)+". Выберите другой диск или освободите место.");}catch(ArgumentException){}catch(IOException){}
            Directory.CreateDirectory(folder);destination.Text=folder;RememberFolder();return true;
        }catch(Exception e){stageLabel.Text="Проверьте папку";progressLabel.Text=UserText.Error(e);return false;}
    }
    async Task RunBatch(){
        if(busy||loading)return;foreach(var job in jobs)job.SelectMode(targetRatio);if(!jobs.Any(j=>j.CanRun))return;string folder;if(!PrepareDestination(out folder))return;
        int ratio=targetRatio;busy=true;closeAfterStop=false;cancellation=new CancellationTokenSource();runStarted=DateTime.UtcNow;lastClock=DateTime.MinValue;pendingProgress=null;progress.Value=0;stageLabel.Text="Начинаем "+ratio+"×…";RefreshButtons();clockTimer.Start();
        try{
            EncodeClip worker=(input,output,update,token)=>WorkerProcess.Reencode(input,output,ratio,update,token);
            var result=await Task.Run(()=>BatchProcessor.Run(jobs.ToArray(),folder,p=>Interlocked.Exchange(ref pendingProgress,p),cancellation.Token,worker,ratio));
            foreach(var job in jobs)UpdateRow(job,null);progress.Value=result.Cancelled?progress.Value:1000;
            stageLabel.Text=result.Cancelled?"Очередь остановлена":(result.Failed==0?"Готово! "+UserText.Clips(result.Completed):"Готово: "+result.Completed+" · ошибок: "+result.Failed);
            progressLabel.Text=result.Cancelled?"Готовые копии сохранены. Нажмите «Сжать», чтобы продолжить.":(result.Failed>0?"Подробности ошибки — в строке исходного клипа.":"Копии в разделе 04. Их можно открыть в Проводнике.");
            timingLabel.Text="За "+UserText.Time(result.Seconds);
        }catch(Exception e){stageLabel.Text="Обработка остановлена";progressLabel.Text=UserText.Error(e);}
        finally{clockTimer.Stop();Interlocked.Exchange(ref pendingProgress,null);busy=false;cancellation.Dispose();cancellation=null;RefreshResults();RefreshQueue();RefreshSpace();if(closeAfterStop)Close();}
    }
    void OnProgress(BatchProgress p){
        var diagnostic=ProgressDiagnostic==null?null:Stopwatch.StartNew();
        if(IsDisposed||Disposing||!busy)return;string live=null;if(p.Job.Status==JobStatus.Running||p.Job.Status==JobStatus.Verifying)live=UserText.Status(p.Job.Status)+(p.Frames>0?" "+p.Frame+"/"+p.Frames:"");UpdateRow(p.Job,live);RefreshResults();
        progress.Value=Math.Max(0,Math.Min(1000,(int)(p.Fraction*1000)));stageLabel.Text=p.Stage+" · "+p.Index+"/"+p.Count;
        string detail=p.Job.Name;if(p.Frames>0&&p.Frame>0)detail+=" · "+p.Frame+"/"+p.Frames;if(p.Fraction>0.03&&p.Fraction<0.99&&p.ElapsedSeconds>5)detail+=" · ещё ≈ "+UserText.Time(p.ElapsedSeconds/p.Fraction*(1-p.Fraction));progressLabel.Text=detail;savedLabel.Text="Сжатие: −"+UserText.Size(Math.Max(0,p.SavedBytes));
        if(diagnostic!=null)ProgressDiagnostic(diagnostic.Elapsed.TotalMilliseconds);
    }
    void StopBatch(){if(loading&&loadCancellation!=null){loadCancellation.Cancel();stageLabel.Text="Останавливаем чтение…";RefreshButtons();return;}if(cancellation==null)return;cancellation.Cancel();stageLabel.Text="Останавливаем обработку…";progressLabel.Text="Готовые копии сохранятся. Незавершённая будет удалена.";RefreshButtons();}
    CompletedClip SelectedOutput(){if(resultGrid.SelectedRows.Count!=1)return null;Guid id=(Guid)resultGrid.SelectedRows[0].Tag;return outputs.FirstOrDefault(o=>o.Id==id);}
    void OpenDestination(){
        try{var selected=SelectedOutput();string folder=selected!=null?Path.GetDirectoryName(selected.OutputPath):destination.Text;if(!Directory.Exists(folder)){progressLabel.Text="Папка недоступна. Проверьте подключение диска.";return;}Process.Start(ExplorerPath,"\""+folder+"\"");}catch(Exception e){progressLabel.Text=UserText.Error(e);}
    }
    void OpenSelectedResult(){
        var selected=SelectedOutput();if(selected==null)return;try{if(!File.Exists(selected.OutputPath)){progressLabel.Text="Готовый файл перемещён или диск недоступен.";return;}Process.Start(ExplorerPath,"/select,\""+selected.OutputPath+"\"");}catch(Exception e){progressLabel.Text=UserText.Error(e);}
    }
    static string ExplorerPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");}}
    void ShowSourceInfo(){if(grid.SelectedRows.Count!=1)return;Guid id=(Guid)grid.SelectedRows[0].Tag;var job=jobs.First(j=>j.Id==id);if(!string.IsNullOrEmpty(job.Error))MessageBox.Show(this,job.Error,"Информация о клипе",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    void ShowAbout(){MessageBox.Show(this,"RAWER 0.6.2 для Windows\n\nRAWER — программа для пакетного сжатия 6K RAW-видео Nikon ZR в формате R3D-NE.\n\nУменьшает размер файлов примерно в 2 или 4 раза, сохраняя разрешение, RAW-метаданные камеры, исходный звук и таймкод. Позволяет добавлять видео или целые папки, выбирать место сохранения, просматривать готовые копии в Проводнике и останавливать обработку. Оригинальные файлы не перезаписываются.\n\nСжатие выполняется с потерями. Итоговый размер и качество зависят от материала и выбранного режима.\n\nАвтор: Александр Шичанин.\n\nСторонний код и лицензии\nДекодер Nikon JPEG XS адаптирован из проекта dnglab (nbuchwitz/dnglab). Адаптированный код распространяется по лицензии GNU LGPL 2.1.\n\nТекст лицензии, авторские уведомления, сведения об источнике и изменениях, а также исходный код включены в полный архив: third_party/, src/ и research/dnglab-reference/.","О RAWER",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    internal void SavePreview(string path,string state,int width,int height=0){
        if(width>0)ClientSize=new Size(width,height>0?height:820);bool four=state.Contains("4")||state=="compare";targetRatio=four?4:2;string example=Path.Combine(Path.GetPathRoot(Environment.SystemDirectory),"Videos");destination.Text=Path.Combine(example,"Compressed");UpdateMode();
        if(state!="empty"){
            int count=state=="many"?24:1;
            for(int n=0;n<count;n++){var job=new ClipJob{SourcePath=Path.Combine(example,"Source","Example_"+(n+1).ToString("000")+".R3D"),SourceBytes=2000000000,Frames=250,Duration=10,TargetRatio=targetRatio};jobs.Add(job);AddRow(job);}
            var first=jobs[0];
            if(state.StartsWith("filled",StringComparison.Ordinal)||state=="compare"){
                if(state=="compare")first.Outputs.Add(new CompletedClip{SourceId=first.Id,SourcePath=first.SourcePath,OutputPath=Path.Combine(destination.Text,"Example_001_RAWER_2x.R3D"),TargetRatio=2,SourceBytes=first.SourceBytes,OutputBytes=1000000000});
                first.Outputs.Add(new CompletedClip{SourceId=first.Id,SourcePath=first.SourcePath,OutputPath=Path.Combine(destination.Text,"Example_001_RAWER_"+targetRatio+"x.R3D"),TargetRatio=targetRatio,SourceBytes=first.SourceBytes,OutputBytes=four?500000000:1000000000});first.Status=JobStatus.Completed;first.OutputPath=first.Outputs.Last().OutputPath;first.OutputBytes=first.Outputs.Last().OutputBytes;UpdateRow(first,null);stageLabel.Text="Готово! 1 клип";progressLabel.Text="Копии в разделе 04. Можно сравнивать в DaVinci.";progress.Value=1000;
            }else if(state.StartsWith("running",StringComparison.Ordinal)){busy=true;cancellation=new CancellationTokenSource();first.Status=JobStatus.Running;UpdateRow(first,"Сжатие 100/250");stageLabel.Text="Сжимаем RAW · 1/1";progressLabel.Text="Example_001.R3D · 100/250 · ещё ≈ 1 мин";progress.Value=376;timingLabel.Text="Прошло 55 с";}
            RefreshResults();RefreshQueue();grid.ClearSelection();resultGrid.ClearSelection();
        }
        ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);Show();CreateChildren(this);PerformLayout();Application.DoEvents();
        if(!string.Equals(Path.GetExtension(path),".png",StringComparison.OrdinalIgnoreCase))throw new IOException("Preview output must use .png.");
        using(var image=new Bitmap(ClientSize.Width,ClientSize.Height)){Controls[0].DrawToBitmap(image,new Rectangle(Point.Empty,ClientSize));using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))image.Save(file,ImageFormat.Png);}busy=false;if(cancellation!=null){cancellation.Dispose();cancellation=null;}Hide();
    }
    protected override bool ShowWithoutActivation {get{return preview||base.ShowWithoutActivation;}}
    static void CreateChildren(Control parent){foreach(Control c in parent.Controls){c.CreateControl();CreateChildren(c);c.PerformLayout();}}
}
}
