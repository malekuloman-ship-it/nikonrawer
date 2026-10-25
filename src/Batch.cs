using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Rawer {
public enum JobStatus { Ready, Running, Verifying, Completed, Cancelled, Failed, Unsupported }
public sealed class CompletedClip {
    public Guid Id=Guid.NewGuid(),SourceId;public string SourcePath,OutputPath;public int TargetRatio;public long SourceBytes,OutputBytes;
    public string Name {get{return Path.GetFileName(OutputPath);}}
}
public static class CompressionMode {
    public static int Require(int ratio){if(ratio!=2&&ratio!=4)throw new ArgumentOutOfRangeException("ratio","Выберите сжатие 2× или 4×.");return ratio;}
}
public sealed class ClipJob {
    public Guid Id=Guid.NewGuid();public string SourcePath,OutputPath,Error;
    public long SourceBytes,OutputBytes;public int Frames;public double Duration;
    public JobStatus Status=JobStatus.Ready;
    public int TargetRatio=2;public readonly List<CompletedClip> Outputs=new List<CompletedClip>();
    public bool CanRun {get{return Status==JobStatus.Ready||Status==JobStatus.Cancelled||Status==JobStatus.Failed;}}
    public string Name {get{return Path.GetFileName(SourcePath);}}
    public void SelectMode(int ratio){
        CompressionMode.Require(ratio);if(Status==JobStatus.Unsupported)return;
        var saved=Outputs.LastOrDefault(o=>o.TargetRatio==ratio&&File.Exists(o.OutputPath));
        if(saved!=null){OutputPath=saved.OutputPath;OutputBytes=saved.OutputBytes;Status=JobStatus.Completed;Error=null;}
        else if(TargetRatio!=ratio||Status==JobStatus.Completed){OutputPath=null;OutputBytes=0;Status=JobStatus.Ready;Error=null;}
        TargetRatio=ratio;
    }
    public static ClipJob Inspect(string path){
        var job=new ClipJob{SourcePath=Path.GetFullPath(path)};
        try {
            using(var movie=new Movie(job.SourcePath)){
                job.SourceBytes=movie.Length;job.Frames=movie.Video.SampleCount;job.Duration=movie.Video.Seconds;
                if(movie.Video.Width!=6060||movie.Video.Height!=3410)throw new NotSupportedException("Поддерживается проверенный режим Nikon ZR 6K.");
            }
        }catch(Exception e){job.Status=JobStatus.Unsupported;job.Error=UserText.Error(e);try{job.SourceBytes=new FileInfo(path).Length;}catch{}}
        return job;
    }
}
public static class UserText {
    public static string Clips(int n){int last=n%10,lastTwo=n%100;return n+" "+(last==1&&lastTwo!=11?"клип":(last>=2&&last<=4&&(lastTwo<12||lastTwo>14)?"клипа":"клипов"));}
    public static string Size(long bytes){if(bytes>=1000000000)return (bytes/1e9).ToString("0.00")+" ГБ";if(bytes>=1000000)return (bytes/1e6).ToString("0.0")+" МБ";if(bytes>=1000)return (bytes/1e3).ToString("0.0")+" КБ";return bytes+" Б";}
    public static string Time(double seconds){if(seconds<1)return "меньше секунды";if(seconds<60)return Math.Ceiling(seconds)+" с";if(seconds<3600)return Math.Ceiling(seconds/60)+" мин";return ((int)seconds/3600)+" ч "+(((int)seconds%3600)/60)+" мин";}
    public static string Status(JobStatus s){switch(s){case JobStatus.Ready:return "В очереди";case JobStatus.Running:return "Сжатие";case JobStatus.Verifying:return "Проверка";case JobStatus.Completed:return "Готово";case JobStatus.Cancelled:return "Отменён";case JobStatus.Failed:return "Ошибка";default:return "Не поддерживается";}}
    public static string Error(Exception e){
        if(e is UnauthorizedAccessException)return "Нет доступа к файлу или папке. Выберите доступную папку.";
        if(e is FileNotFoundException)return "Исходный файл не найден. Подключите диск и повторите попытку.";
        if(e is DirectoryNotFoundException)return "Папка недоступна. Проверьте подключение диска.";
        if(e is PathTooLongException)return "Слишком длинный путь. Выберите папку ближе к корню диска.";
        if(e is NotSupportedException)return "Этот профиль файла не поддерживается. Проверен Nikon ZR 6K. "+e.Message;
        if(e is IOException)return "Не удалось прочитать или записать файл. "+e.Message;
        return e.Message;
    }
}
public static class OutputNames {
    public static string Next(string input,string folder,ISet<string> reserved,int ratio=2){
        CompressionMode.Require(ratio);string stem=Path.GetFileNameWithoutExtension(input)+"_RAWER_"+ratio+"x";string full=Path.GetFullPath(folder);
        for(int i=1;i<100000;i++){
            string name=stem+(i==1?"":" ("+i+")")+".R3D",path=Path.Combine(full,name);
            if(string.Equals(Path.GetFullPath(input),path,StringComparison.OrdinalIgnoreCase)||File.Exists(path)||Directory.Exists(path)||File.Exists(path+".report.json")||Directory.Exists(path+".report.json")||(reserved!=null&&reserved.Contains(path)))continue;
            if(reserved!=null)reserved.Add(path);return path;
        }
        throw new IOException("Не удалось подобрать свободное имя результата.");
    }
}
public sealed class AppSettings {
    public string DestinationFolder,LastInputFolder;
    public int TargetRatio=2;
    public static string DefaultPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RAWER","settings.json");}}
    public static AppSettings Load(string path){try{if(File.Exists(path)&&new FileInfo(path).Length<65536)return new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(path,Encoding.UTF8))??new AppSettings();}catch{}return new AppSettings();}
    public void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
}
public static class InputFiles {
    public static List<string> Collect(IEnumerable<string> paths,string outputFolder,CancellationToken cancellation=default(CancellationToken)){
        var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string excluded=null;try{if(!string.IsNullOrWhiteSpace(outputFolder))excluded=Path.GetFullPath(outputFolder).TrimEnd(Path.DirectorySeparatorChar);}catch{}
        foreach(string original in paths){
            cancellation.ThrowIfCancellationRequested();
            if(File.Exists(original)){if(string.Equals(Path.GetExtension(original),".r3d",StringComparison.OrdinalIgnoreCase))files.Add(Path.GetFullPath(original));continue;}
            if(!Directory.Exists(original))continue;var stack=new Stack<string>();string inputRoot=Path.GetFullPath(original);stack.Push(inputRoot);
            while(stack.Count>0){cancellation.ThrowIfCancellationRequested();string dir=stack.Pop();if(!string.Equals(dir,inputRoot,StringComparison.OrdinalIgnoreCase)&&string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar),excluded,StringComparison.OrdinalIgnoreCase))continue;
                try{
                    foreach(string f in Directory.EnumerateFiles(dir)){cancellation.ThrowIfCancellationRequested();if(string.Equals(Path.GetExtension(f),".r3d",StringComparison.OrdinalIgnoreCase)&&!HasOwnReport(f))files.Add(Path.GetFullPath(f));}
                    foreach(string d in Directory.EnumerateDirectories(dir))if((File.GetAttributes(d)&FileAttributes.ReparsePoint)==0)stack.Push(d);
                }catch(UnauthorizedAccessException){}catch(IOException){}
            }
        }
        return files.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
    }
    static bool HasOwnReport(string file){try{string p=file+".report.json";if(!File.Exists(p)||new FileInfo(p).Length>1024*1024)return false;var d=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(p)) as Dictionary<string,object>;return d!=null&&d.ContainsKey("prototype")&&Convert.ToString(d["prototype"]).StartsWith("RAWER",StringComparison.Ordinal);}catch{return false;}}
}
public sealed class BatchProgress {
    public ClipJob Job;public int Index,Count,Frame,Frames;public double Fraction,ElapsedSeconds;public string Stage;public long SavedBytes;public int Finished,Failed;
}
public sealed class BatchResult {public int Completed,Failed;public bool Cancelled;public long SourceBytes,OutputBytes;public double Seconds;}
public delegate object EncodeClip(string source,string output,Action<ProgressInfo> progress,CancellationToken cancellation);
public static class BatchProcessor {
    public static BatchResult Run(IList<ClipJob> jobs,string folder,Action<BatchProgress> update,CancellationToken cancellation,EncodeClip encoder=null,int targetRatio=2){
        CompressionMode.Require(targetRatio);foreach(var job in jobs)job.SelectMode(targetRatio);
        folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);
        var work=jobs.Where(j=>j.CanRun).ToList();var result=new BatchResult();var watch=Stopwatch.StartNew();var reserved=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double total=Math.Max(1,work.Sum(j=>(double)Math.Max(1,j.SourceBytes))),processed=0;
        if(encoder==null)encoder=(source,dest,progress,token)=>Optimizer.Reencode(source,dest,targetRatio,progress,token);
        for(int index=0;index<work.Count;index++){
            if(cancellation.IsCancellationRequested){result.Cancelled=true;break;}ClipJob job=work[index];int captured=index;double weight=Math.Max(1,job.SourceBytes);job.Error=null;job.OutputBytes=0;
            long currentSaved=0;Action<ProgressInfo> report=p=>{
                if(p.Removed>0)currentSaved=p.Removed;
                bool verify=p.Stage.StartsWith("Проверка",StringComparison.Ordinal);job.Status=verify?JobStatus.Verifying:JobStatus.Running;
                double local=p.Frames>0?Math.Max(0,Math.Min(1,(double)p.Frame/p.Frames)):0;
                if(update!=null)update(new BatchProgress{Job=job,Index=captured+1,Count=work.Count,Frame=p.Frame,Frames=p.Frames,Stage=verify?"Проверяем сохранённую копию":"Сжимаем RAW",Fraction=(processed+weight*(verify?0.94+0.06*local:0.94*local))/total,ElapsedSeconds=watch.Elapsed.TotalSeconds,SavedBytes=result.SourceBytes-result.OutputBytes+currentSaved,Finished=result.Completed,Failed=result.Failed});
            };
            try {
                job.OutputPath=OutputNames.Next(job.SourcePath,folder,reserved,targetRatio);job.Status=JobStatus.Running;report(new ProgressInfo{Frames=job.Frames,Stage="Сжатие"});
                encoder(job.SourcePath,job.OutputPath,report,cancellation);
                if(!File.Exists(job.OutputPath))throw new IOException("Результат не появился в выбранной папке.");job.OutputBytes=new FileInfo(job.OutputPath).Length;
                job.Outputs.Add(new CompletedClip{SourceId=job.Id,SourcePath=job.SourcePath,OutputPath=job.OutputPath,TargetRatio=targetRatio,SourceBytes=job.SourceBytes,OutputBytes=job.OutputBytes});
                job.Status=JobStatus.Completed;result.Completed++;result.SourceBytes+=job.SourceBytes;result.OutputBytes+=job.OutputBytes;
            }catch(OperationCanceledException){job.Status=JobStatus.Cancelled;result.Cancelled=true;if(update!=null)update(new BatchProgress{Job=job,Index=index+1,Count=work.Count,Stage="Обработка отменена",Fraction=processed/total,ElapsedSeconds=watch.Elapsed.TotalSeconds,Finished=result.Completed,Failed=result.Failed,SavedBytes=result.SourceBytes-result.OutputBytes});break;}
            catch(Exception e){job.Status=JobStatus.Failed;job.Error=UserText.Error(e);result.Failed++;}
            processed+=weight;
            if(update!=null)update(new BatchProgress{Job=job,Index=index+1,Count=work.Count,Stage=job.Status==JobStatus.Completed?"Файл готов":"Ошибка обработки файла",Fraction=processed/total,ElapsedSeconds=watch.Elapsed.TotalSeconds,SavedBytes=result.SourceBytes-result.OutputBytes,Finished=result.Completed,Failed=result.Failed});
        }
        result.Seconds=watch.Elapsed.TotalSeconds;return result;
    }
}
}
