using System;
using System.IO;
using System.Threading;
namespace Rawer {
internal static class WorkerProcessCheck {
    public static void Run(string source){
        string basePath=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".tests"));Directory.CreateDirectory(basePath);string root=Path.Combine(basePath,"RAWER-worker-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var original=new FileInfo(source);long bytes=original.Length;DateTime modified=original.LastWriteTimeUtc;
        try{
            using(var token=new CancellationTokenSource()){
                token.Cancel();bool cancelled=false;try{WorkerProcess.Reencode(source,Path.Combine(root,"pre.R3D"),4,null,token.Token);}catch(OperationCanceledException){cancelled=true;}
                if(!cancelled||Directory.GetFiles(root).Length!=0)throw new Exception("Pre-cancelled worker produced output.");Console.WriteLine("PASS pre-cancelled worker does not start or publish a file");
            }
            using(var token=new CancellationTokenSource()){
                token.CancelAfter(80);bool cancelled=false;try{WorkerProcess.Reencode(source,Path.Combine(root,"early.R3D"),4,null,token.Token);}catch(OperationCanceledException){cancelled=true;}
                if(!cancelled||Directory.GetFiles(root).Length!=0)throw new Exception("Worker cancellation left a partial file.");Console.WriteLine("PASS worker stops during startup or encoding without partial files");
            }
            using(var token=new CancellationTokenSource()){
                bool cancelled=false;try{WorkerProcess.Reencode(source,Path.Combine(root,"frame.R3D"),4,p=>{if(p.Frame>=1)token.Cancel();},token.Token);}catch(OperationCanceledException){cancelled=true;}
                if(!cancelled||Directory.GetFiles(root).Length!=0)throw new Exception("Worker cancellation after first frame left output.");Console.WriteLine("PASS hidden worker stops cooperatively after first encoded frame and removes its partial file");
            }
            var after=new FileInfo(source);if(after.Length!=bytes||after.LastWriteTimeUtc!=modified)throw new Exception("Source changed during worker checks.");Console.WriteLine("PASS worker cancellation leaves the original size and timestamp unchanged");
        }finally{
            string checkedRoot=Path.GetFullPath(root);if(!string.Equals(Path.GetDirectoryName(checkedRoot),basePath,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(checkedRoot).StartsWith("RAWER-worker-",StringComparison.Ordinal))throw new InvalidOperationException("Unsafe worker test cleanup path.");
            if(Directory.Exists(checkedRoot))Directory.Delete(checkedRoot,true);
        }
    }
}
}
