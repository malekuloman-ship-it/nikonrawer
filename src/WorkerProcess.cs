using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Rawer {
// Keep coefficient heaps and their GC pauses out of the interactive process.
// The same executable runs as a hidden worker; no install or external codec is
// needed. Closing the parent pipe requests cooperative cancellation.
internal static class WorkerProcess {
    static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
    public static object Reencode(string input,string output,int ratio,Action<ProgressInfo> progress,CancellationToken cancellation){
        cancellation.ThrowIfCancellationRequested();CompressionMode.Require(ratio);
        var start=new ProcessStartInfo{
            FileName=Assembly.GetEntryAssembly().Location,
            Arguments="--worker-reencode "+Quote(Path.GetFullPath(input))+" "+Quote(Path.GetFullPath(output))+" "+ratio,
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        };
        var errors=new StringBuilder();bool resultReceived=false;Exception callbackError=null;var parser=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
        using(var process=new Process{StartInfo=start}){
            process.OutputDataReceived+=(s,e)=>{
                if(e.Data==null)return;
                try{if(e.Data.StartsWith("PROGRESS ",StringComparison.Ordinal)){var p=parser.Deserialize<ProgressInfo>(e.Data.Substring(9));if(progress!=null)progress(p);}else if(e.Data.StartsWith("RESULT ",StringComparison.Ordinal))resultReceived=true;}
                catch(Exception error){callbackError=error;try{process.StandardInput.WriteLine("cancel");process.StandardInput.Flush();}catch{}}
            };
            process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null&&errors.Length<16384)errors.AppendLine(e.Data);};
            if(!process.Start())throw new IOException("Не удалось запустить обработку видео.");
            process.BeginOutputReadLine();process.BeginErrorReadLine();
            using(cancellation.Register(()=>{try{if(!process.HasExited){process.StandardInput.WriteLine("cancel");process.StandardInput.Flush();}}catch(InvalidOperationException){}catch(IOException){}}))process.WaitForExit();
            // WaitForExit() also drains both asynchronous readers.
            if(callbackError!=null)throw new IOException("Не удалось прочитать ход обработки.",callbackError);
            if(process.ExitCode!=0){if(cancellation.IsCancellationRequested||process.ExitCode==130)throw new OperationCanceledException(cancellation);throw new IOException(errors.Length>0?errors.ToString().Trim():"Обработка видео неожиданно завершилась.");}
            if(!resultReceived||!File.Exists(output))throw new IOException("Обработчик не подтвердил сохранение файла.");
            return null;
        }
    }
    public static int Run(string input,string output,int ratio){
        // A windowed executable has no console. Use the redirected handles
        // directly instead of Console.*Encoding, which calls console-only APIs.
        var inputPipe=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false));
        var outputPipe=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
        var errorPipe=new StreamWriter(Console.OpenStandardError(),new UTF8Encoding(false)){AutoFlush=true};
        var token=new CancellationTokenSource();
        var reader=new Thread(()=>{
            try{string line;while((line=inputPipe.ReadLine())!=null){if(line=="cancel"){token.Cancel();return;}}token.Cancel();}
            catch(IOException){token.Cancel();}catch(ObjectDisposedException){}
        }){IsBackground=true,Name="RAWER cancellation pipe"};reader.Start();
        try{
            var result=Optimizer.Reencode(input,output,ratio,p=>outputPipe.WriteLine("PROGRESS "+Optimizer.Json(p)),token.Token);
            outputPipe.WriteLine("RESULT "+Optimizer.Json(result));return 0;
        }catch(OperationCanceledException){errorPipe.WriteLine("CANCELLED");return 130;}
        catch(Exception e){errorPipe.WriteLine("ERROR "+e.Message);return 1;}
    }
}
}
