using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Rawer {
// Only the caller accesses Movie/its stream and publishes bytes. A bounded
// window of independent frames is encoded in parallel and consumed in order.
internal sealed class FramePipeline : IDisposable {
    sealed class Pending {public int Index;public Task<EncodedRawFrame> Task;}
    readonly Movie source;readonly int budget,workers;readonly int[] order;
    readonly Queue<Pending> pending=new Queue<Pending>();readonly CancellationTokenSource cancellation;
    int scheduled,lastHint=25;bool disposed;
    public int Workers {get{return workers;}}
    public FramePipeline(Movie source,int budget,CancellationToken token,int requestedWorkers=0,int frameLimit=0){
        this.source=source;this.budget=budget;workers=requestedWorkers==0?AutomaticWorkers:Math.Max(1,Math.Min(6,requestedWorkers));
        var ordered=source.Video.Chunks.OrderBy(c=>c.Offset).Select(c=>c.FirstSample);order=(frameLimit>0?ordered.Take(frameLimit):ordered).ToArray();cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);
    }
    void Fill(){
        while(pending.Count<workers&&scheduled<order.Length){
            cancellation.Token.ThrowIfCancellationRequested();int index=order[scheduled++];byte[] frame=source.ReadFrame(index);var token=cancellation.Token;int initialHint=lastHint;
            pending.Enqueue(new Pending{Index=index,Task=Task.Factory.StartNew(()=>{
                var decoded=RawEntropy.Decode(frame,token);int hint=initialHint;return RawEntropy.ForBudget(decoded,budget,ref hint,token);
            },CancellationToken.None,TaskCreationOptions.None,TaskScheduler.Default)});
        }
    }
    public EncodedRawFrame Take(int index){
        if(disposed)throw new ObjectDisposedException("FramePipeline");Fill();
        if(pending.Count==0||pending.Peek().Index!=index)throw new InvalidOperationException("Unexpected video frame order.");
        var job=pending.Dequeue();var encoded=job.Task.GetAwaiter().GetResult();
        cancellation.Token.ThrowIfCancellationRequested();lastHint=encoded.Steps;Fill();return encoded;
    }
    public void Dispose(){
        if(disposed)return;disposed=true;cancellation.Cancel();
        try{Task.WhenAll(pending.Select(p=>p.Task)).GetAwaiter().GetResult();}catch(OperationCanceledException){}catch(Exception){}
        pending.Clear();cancellation.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)]
    sealed class MemoryStatus {
        public uint Length=(uint)Marshal.SizeOf(typeof(MemoryStatus)),Load;
        public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx([In,Out]MemoryStatus status);
    internal static int AutomaticWorkers {
        get{
            int cpu=Math.Min(6,Math.Max(1,Environment.ProcessorCount/2));
            try{var status=new MemoryStatus();if(GlobalMemoryStatusEx(status)){long reserve=1536L*1024*1024;long available=(long)Math.Min((ulong)long.MaxValue,status.AvailablePhysical);return Math.Max(1,Math.Min(cpu,(int)Math.Max(0,(available-reserve)/(512L*1024*1024))));}}catch{}
            return Math.Min(cpu,2);
        }
    }
}
}
