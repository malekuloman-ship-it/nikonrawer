using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Rawer {
internal static class Program {
    [STAThread]
    public static int Main(string[] args) {
        if(args.Length>=2&&args[0]=="--preview"){
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
            using(var form=new MainForm(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"preview-settings.json"),true))form.SavePreview(args[1],args.Length>2?args[2]:"empty",args.Length>3?int.Parse(args[3]):1100,args.Length>4?int.Parse(args[4]):0);return 0;
        }
        if(args.Length>0&&args.All(p=>File.Exists(p)||Directory.Exists(p))){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var form=new MainForm();form.Shown+=async(s,e)=>await form.AddPaths(args);Application.Run(form);return 0;}
        if(args.Length==0){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());return 0;}
        try {
            string command=args[0].ToLowerInvariant();
            if(command=="--worker-reencode"&&args.Length==4)return WorkerProcess.Run(args[1],args[2],CompressionMode.Require(int.Parse(args[3])));
            if(command=="inspect"&&args.Length==2){using(var m=new Movie(args[1]))Console.WriteLine(Optimizer.Json(m.Summary()));return 0;}
            if(command=="queue-check"&&args.Length==2){var job=ClipJob.Inspect(args[1]);Console.WriteLine(Optimizer.Json(new{ready=job.CanRun,frames=job.Frames,sourceBytes=job.SourceBytes,status=UserText.Status(job.Status),error=job.Error}));return job.CanRun?0:1;}
            if(command=="app-check"&&args.Length>=2){BatchSelfTest.RealCancellation(args[1],args.Length>2?int.Parse(args[2]):2);return 0;}
            if(command=="worker-check"&&args.Length==2){WorkerProcessCheck.Run(args[1]);return 0;}
            if(command=="worker-encode"&&args.Length==4){WorkerProcess.Reencode(args[1],args[2],CompressionMode.Require(int.Parse(args[3])),null,CancellationToken.None);Console.WriteLine("PASS complete hidden worker conversion");return 0;}
            if(command=="perf-probe"&&args.Length>=2){PerformanceCheck.Run(args[1],args.Length>2?args[2]:null,args.Length>3?args[3]:"0,109,218");return 0;}
            if(command=="pipeline-probe"&&args.Length==2){PerformanceCheck.Pipeline(args[1]);return 0;}
            if(command=="size-check"&&args.Length==2){PerformanceCheck.Sizes(args[1]);return 0;}
            if(command=="selftest"){SelfTest.Run();BatchSelfTest.Run();EntropySelfTest.Run();return 0;}
            if(command=="privacy-check"&&args.Length==2){PrivacySelfTest.InspectSummary(args[1]);return 0;}
            if(command=="ui-check"){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var form=new MainForm(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-check-settings.json"),true))form.RunInterfaceChecks();return 0;}
            if(command=="codec-check"&&args.Length==2){
                using(var m=new Movie(args[1])){var watch=System.Diagnostics.Stopwatch.StartNew();var f=RawEntropy.Decode(m.ReadFrame(0));long coefficients=0,nonzero=0;int max=0;foreach(var p in f.Precincts)foreach(var band in p.Bands)foreach(var v in band.C){coefficients++;if((v&32767)!=0)nonzero++;max=Math.Max(max,v&32767);}Console.WriteLine(Optimizer.Json(new{decodedFirstFrame=true,precincts=f.Count,coefficients=coefficients,nonzero=nonzero,maximumMagnitude=max,milliseconds=watch.ElapsedMilliseconds}));}return 0;
            }
            if(command=="encode-probe"&&args.Length>=2){
                using(var m=new Movie(args[1])){int index=args.Length>2?int.Parse(args[2]):0;var watch=System.Diagnostics.Stopwatch.StartNew();var f=RawEntropy.Decode(m.ReadFrame(index));foreach(int steps in new[]{0,25,50,75}){var e=RawEntropy.Encode(f,steps,false);RawEntropy.VerifyEncoded(f,e);Console.WriteLine(Optimizer.Json(new{frame=index,qualitySteps=steps,bytes=e.Bytes.Length,ratio=(double)f.Source.Length/e.Bytes.Length,coefficientRoundTrip=true,coefficientRmsError=Math.Sqrt(e.SquaredError/e.CoefficientCount),maximumCoefficientError=e.MaximumError,milliseconds=watch.ElapsedMilliseconds}));}}return 0;
            }
            if(command=="budget-probe"&&args.Length==2){
                using(var m=new Movie(args[1])){int budget=(int)((m.Length/2-(m.Length-m.Video.Chunks.Sum(c=>c.Length)))/m.Video.SampleCount),hint=25;var watch=System.Diagnostics.Stopwatch.StartNew();var frame=RawEntropy.Decode(m.ReadFrame(0));var e=RawEntropy.ForBudget(frame,budget,ref hint);Console.WriteLine(Optimizer.Json(new{targetFrameBytes=budget,encodedBytes=e.Bytes.Length,qualitySteps=e.Steps,coefficientRoundTrip=true,coefficientRmsError=Math.Sqrt(e.SquaredError/e.CoefficientCount),maximumCoefficientError=e.MaximumError,milliseconds=watch.ElapsedMilliseconds}));}return 0;
            }
            if((command=="compact"||command=="reencode"||command=="verify")&&args.Length>=3){
                double target=2;bool quiet=false;
                for(int i=3;i<args.Length;i++){if(args[i]=="--quiet")quiet=true;else if(args[i]=="--target-ratio"&&i+1<args.Length)target=double.Parse(args[++i],CultureInfo.InvariantCulture);else throw new ArgumentException("Unknown option: "+args[i]);}
                Action<ProgressInfo> progress=p=>{if(!quiet&&(p.Frame==1||p.Frame%25==0||p.Frame==p.Frames))Console.WriteLine("PROGRESS "+Optimizer.Json(new{stage=p.Stage,frame=p.Frame,frames=p.Frames,removedBytes=p.Removed}));};
                object result=command=="reencode"?Optimizer.Reencode(args[1],args[2],target,progress,CancellationToken.None):(command=="compact"?Optimizer.Compact(args[1],args[2],target,progress,CancellationToken.None):Optimizer.Verify(args[1],args[2],progress,CancellationToken.None));
                Console.WriteLine("RESULT "+Optimizer.Json(result));return 0;
            }
            Console.WriteLine("RAWER 0.6.2 - Nikon ZR 6K RAW compressor\n  RAWER.exe [INPUT.R3D ...]\n  RAWER.Cli.exe inspect INPUT.R3D\n  RAWER.Cli.exe reencode INPUT.R3D OUTPUT.R3D [--target-ratio 2|4] [--quiet]\n  RAWER.Cli.exe compact INPUT.R3D OUTPUT.R3D\n  RAWER.Cli.exe encode-probe INPUT.R3D [FRAME]\n  RAWER.Cli.exe selftest");return 2;
        } catch(Exception e){Console.Error.WriteLine("ERROR "+(args.Length>0&&args[0]=="selftest"?e.ToString():e.Message));return 1;}
    }
}
}
