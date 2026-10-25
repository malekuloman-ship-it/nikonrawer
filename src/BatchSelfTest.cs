using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Rawer {
internal static class BatchSelfTest {
    static int passed;static void Check(bool ok,string name){if(!ok)throw new Exception("Batch test failed: "+name);passed++;Console.WriteLine("PASS "+name);}
    public static void Run(){
        passed=0;string temporary=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".tests"));Directory.CreateDirectory(temporary);string root=Path.Combine(temporary,"RAWER-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string output=Path.Combine(root,"Сжатые RAW");Directory.CreateDirectory(output);string source=Path.Combine(root,"DSC.R3D");File.WriteAllBytes(source,new byte[100]);
            string occupied=Path.Combine(output,"DSC_RAWER_2x.R3D");File.WriteAllText(occupied,"KEEP");File.WriteAllText(Path.Combine(output,"DSC_RAWER_2x (2).R3D.report.json"),"{}");
            var reserved=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string next=OutputNames.Next(source,output,reserved),duplicate=OutputNames.Next(source,output,reserved);
            Check(Path.GetFileName(next)=="DSC_RAWER_2x (3).R3D"&&Path.GetFileName(duplicate)=="DSC_RAWER_2x (4).R3D","output naming avoids files, reports and duplicate clip names");Check(File.ReadAllText(occupied)=="KEEP","existing result remains untouched");
            Directory.CreateDirectory(Path.Combine(output,"DSC_RAWER_2x (5).R3D"));Check(Path.GetFileName(OutputNames.Next(source,output,reserved))=="DSC_RAWER_2x (6).R3D","output naming also avoids existing directories");
            string scan=Path.Combine(root,"scan"),nested=Path.Combine(scan,"nested"),excluded=Path.Combine(scan,"compressed");Directory.CreateDirectory(nested);Directory.CreateDirectory(excluded);File.WriteAllText(Path.Combine(scan,"one.R3D"),"1");File.WriteAllText(Path.Combine(nested,"two.r3d"),"2");File.WriteAllText(Path.Combine(excluded,"three.R3D"),"3");string prior=Path.Combine(scan,"old.R3D");File.WriteAllText(prior,"4");File.WriteAllText(prior+".report.json","{\"prototype\":\"RAWER 0.2\"}");
            var found=InputFiles.Collect(new[]{scan,Path.Combine(scan,"one.R3D")},excluded);Check(found.Count==2&&found.Any(x=>x.EndsWith("two.r3d")),"folder scan is recursive, deduplicated and skips output copies");Check(InputFiles.Collect(new[]{scan},scan).Count==3,"source folder may also be the chosen output folder");
            string prefs=Path.Combine(root,"prefs","settings.json");var settings=new AppSettings{DestinationFolder=output,LastInputFolder=nested};settings.Save(prefs);settings.DestinationFolder=Path.Combine(root,"Другая папка");settings.Save(prefs);Check(AppSettings.Load(prefs).DestinationFolder==settings.DestinationFolder,"last output folder persists with Unicode paths");File.WriteAllText(prefs,"broken");Check(AppSettings.Load(prefs)!=null,"damaged preferences do not block startup");
            var jobs=Enumerable.Range(0,3).Select(i=>new ClipJob{SourcePath=Path.Combine(root,"camera"+i,"clip.R3D"),SourceBytes=100,Frames=10}).ToArray();string batch=Path.Combine(root,"batch");int calls=0;var cts=new CancellationTokenSource();
            EncodeClip encoder=(input,dest,progress,token)=>{calls++;if(calls==2){cts.Cancel();throw new OperationCanceledException(token);}File.WriteAllBytes(dest,new byte[50]);progress(new ProgressInfo{Frame=10,Frames=10,Removed=50,Stage="Сжатие"});progress(new ProgressInfo{Frame=10,Frames=10,Stage="Проверка"});return null;};
            var progressSaved=new List<long>();var first=BatchProcessor.Run(jobs,batch,p=>progressSaved.Add(p.SavedBytes),cts.Token,encoder);
            Check(first.Cancelled&&first.Completed==1&&jobs[0].Status==JobStatus.Completed&&jobs[1].Status==JobStatus.Cancelled&&jobs[2].Status==JobStatus.Ready,"cancellation preserves completed files and leaves remaining clips runnable");
            string firstOutput=jobs[0].OutputPath;Check(File.Exists(firstOutput)&&jobs[1].OutputPath!=null&&!File.Exists(jobs[1].OutputPath),"cancelled clip has no published result");
            int resumed=0;var second=BatchProcessor.Run(jobs,batch,null,CancellationToken.None,(input,dest,p,token)=>{resumed++;File.WriteAllBytes(dest,new byte[50]);return null;});
            Check(resumed==2&&second.Completed==2&&jobs.All(j=>j.Status==JobStatus.Completed),"resume skips completed clips");Check(jobs.Select(j=>j.OutputPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()==3&&File.Exists(firstOutput),"identical basenames are saved as separate files");
            var errors=new[]{new ClipJob{SourcePath="bad.R3D",SourceBytes=100},new ClipJob{SourcePath="good.R3D",SourceBytes=100}};var continuation=BatchProcessor.Run(errors,Path.Combine(root,"errors"),null,CancellationToken.None,(input,dest,p,token)=>{if(input=="bad.R3D")throw new InvalidDataException("Invalid sample");File.WriteAllBytes(dest,new byte[50]);return null;});
            Check(continuation.Failed==1&&continuation.Completed==1&&errors[1].Status==JobStatus.Completed,"one failed clip does not discard the rest of the queue");
            var stopped=new CancellationTokenSource();stopped.Cancel();int started=0;var untouched=new[]{new ClipJob{SourcePath=source,SourceBytes=100}};var none=BatchProcessor.Run(untouched,Path.Combine(root,"stopped"),null,stopped.Token,(a,b,p,t)=>{started++;return null;});Check(none.Cancelled&&started==0&&untouched[0].Status==JobStatus.Ready,"pre-cancelled batch starts no conversions");
            Check(new FileInfo(source).Length==100,"source files are unchanged by queue operations");
            bool scanCancelled=false;try{InputFiles.Collect(new[]{scan},output,stopped.Token);}catch(OperationCanceledException){scanCancelled=true;}Check(scanCancelled,"folder scanning can be cancelled");
            Check(Path.GetFileName(OutputNames.Next(source,output,reserved,4))=="DSC_RAWER_4x.R3D","4x output names identify the selected compression mode");
            bool invalid=false;try{OutputNames.Next(source,output,reserved,3);}catch(ArgumentOutOfRangeException){invalid=true;}Check(invalid,"unsupported UI compression modes are rejected");
            settings.TargetRatio=4;settings.Save(prefs);Check(AppSettings.Load(prefs).TargetRatio==4,"compression choice persists with the output folder");File.WriteAllText(prefs,"{\"DestinationFolder\":\"C:\\\\ExampleMedia\"}");Check(AppSettings.Load(prefs).TargetRatio==2,"older settings default to 2x");
            var compare=new ClipJob{SourcePath=source,SourceBytes=100,Frames=1};string comparison=Path.Combine(root,"comparison");int conversions=0;
            EncodeClip modeEncoder=(input,dest,p,token)=>{conversions++;if(compare.TargetRatio==4&&!dest.EndsWith("_4x.R3D",StringComparison.OrdinalIgnoreCase))throw new Exception("4x did not reach output naming.");File.WriteAllBytes(dest,new byte[compare.TargetRatio==4?25:50]);return null;};
            var four=BatchProcessor.Run(new[]{compare},comparison,null,CancellationToken.None,modeEncoder,4);string fourPath=compare.OutputPath;
            Check(four.Completed==1&&compare.TargetRatio==4&&compare.OutputBytes==25&&compare.Outputs.Single().TargetRatio==4,"4x reaches the batch encoder and records its result");
            var two=BatchProcessor.Run(new[]{compare},comparison,null,CancellationToken.None,modeEncoder,2);
            Check(two.Completed==1&&compare.Outputs.Count==2&&compare.Outputs.Select(o=>o.TargetRatio).Distinct().Count()==2&&File.Exists(fourPath),"switching to 2x produces a separate copy and retains the 4x result");
            int beforeRepeat=conversions;var repeated=BatchProcessor.Run(new[]{compare},comparison,null,CancellationToken.None,modeEncoder,4);
            Check(repeated.Completed==0&&conversions==beforeRepeat&&compare.OutputPath==fourPath&&!compare.CanRun,"switching back restores the finished mode without re-encoding");
            File.Delete(fourPath);compare.SelectMode(4);Check(compare.CanRun,"a moved or deleted output can be regenerated");
            Console.WriteLine("BATCH SELFTEST "+passed+" passed");
        }finally{
            string checkedRoot=Path.GetFullPath(root);if(!string.Equals(Path.GetDirectoryName(checkedRoot).TrimEnd(Path.DirectorySeparatorChar),temporary.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(checkedRoot).StartsWith("RAWER-tests-",StringComparison.Ordinal))throw new InvalidOperationException("Unsafe test cleanup path.");
            if(Directory.Exists(checkedRoot))Directory.Delete(checkedRoot,true);
        }
    }
    public static void RealCancellation(string source,int ratio=2){
        string temporary=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".tests"));Directory.CreateDirectory(temporary);string root=Path.Combine(temporary,"RAWER-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var before=new FileInfo(source);long size=before.Length;DateTime modified=before.LastWriteTimeUtc;var job=ClipJob.Inspect(source);if(!job.CanRun)throw new InvalidDataException(job.Error);var cancellation=new CancellationTokenSource();
        try{
            var result=BatchProcessor.Run(new[]{job},root,p=>{if(p.Frame>=1)cancellation.Cancel();},cancellation.Token,null,ratio);
            if(!result.Cancelled||result.Completed!=0||job.Status!=JobStatus.Cancelled||Directory.GetFiles(root).Length!=0)throw new Exception("Real cancellation left an output or did not stop.");
            var after=new FileInfo(source);if(after.Length!=size||after.LastWriteTimeUtc!=modified)throw new Exception("Source changed during cancellation.");
            Console.WriteLine("PASS real encoder cancelled after the first frame; no partial or final output remains; source size and timestamp unchanged");
            cancellation.Dispose();cancellation=new CancellationTokenSource();cancellation.CancelAfter(50);var watch=System.Diagnostics.Stopwatch.StartNew();
            var early=BatchProcessor.Run(new[]{ClipJob.Inspect(source)},root,null,cancellation.Token,null,ratio);
            if(!early.Cancelled||early.Completed!=0||Directory.GetFiles(root).Length!=0||watch.Elapsed.TotalSeconds>10)throw new Exception("Cancellation during parallel encoding failed or left temporary files.");
            Console.WriteLine("PASS cancellation during parallel encoding stops workers and removes temporary output");
        }finally{cancellation.Dispose();string checkedRoot=Path.GetFullPath(root);if(!string.Equals(Path.GetDirectoryName(checkedRoot).TrimEnd(Path.DirectorySeparatorChar),temporary.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(checkedRoot).StartsWith("RAWER-tests-",StringComparison.Ordinal))throw new InvalidOperationException("Unsafe test cleanup path.");if(Directory.Exists(checkedRoot))Directory.Delete(checkedRoot,true);}
    }
}
}
