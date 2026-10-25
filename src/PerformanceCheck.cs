using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Rawer {
internal static class PerformanceCheck {
    public static void Pipeline(string input){
        foreach(int workers in new[]{4,6}){
            GC.Collect();GC.WaitForPendingFinalizers();var watch=Stopwatch.StartNew();long bytes=0;int gc0=GC.CollectionCount(0),gc2=GC.CollectionCount(2);
            using(var movie=new Movie(input)){
                int budget=(int)((movie.Length/2-(movie.Length-movie.Video.Chunks.Sum(c=>c.Length)))/movie.Video.SampleCount);
                using(var pipeline=new FramePipeline(movie,budget,System.Threading.CancellationToken.None,workers,16))for(int i=0;i<16;i++)bytes+=pipeline.Take(i).Bytes.Length;
            }
            Console.WriteLine(Optimizer.Json(new{workers=workers,frames=16,milliseconds=watch.Elapsed.TotalMilliseconds,bytes=bytes,gen0=GC.CollectionCount(0)-gc0,gen2=GC.CollectionCount(2)-gc2,managedBytes=GC.GetTotalMemory(false)}));
        }
    }
    public static void Sizes(string input){
        using(var m=new Movie(input))foreach(int index in new[]{0,109,218}){
            var frame=RawEntropy.Decode(m.ReadFrame(index));foreach(int steps in new[]{0,1,24,25,26,42,43,44,45,50,75,200}){
                int size=RawEntropy.EncodedSize(frame,steps);var actual=RawEntropy.Encode(frame,steps,false);if(size!=actual.Bytes.Length)throw new Exception("Rate estimate mismatch at frame "+index+", steps "+steps);
            }
        }
        Console.WriteLine("PASS exact rate estimates match 36 complete encodes across frames and quality boundaries");
    }
    public static void Run(string input,string reference,string indices){
        var samples=new List<object>();
        using(var movie=new Movie(input))using(var previous=string.IsNullOrEmpty(reference)?null:new Movie(reference)){
            int budget=(int)((movie.Length/2-(movie.Length-movie.Video.Chunks.Sum(c=>c.Length)))/movie.Video.SampleCount);
            foreach(int index in indices.Split(',').Select(int.Parse)){
                GC.Collect();GC.WaitForPendingFinalizers();
                var clock=Stopwatch.StartNew();var frame=RawEntropy.Decode(movie.ReadFrame(index));double decode=clock.Elapsed.TotalMilliseconds;
                int hint=25;var encoded=RawEntropy.ForBudget(frame,budget,ref hint);double total=clock.Elapsed.TotalMilliseconds;
                bool? identical=previous==null?(bool?)null:Bytes.Equal(encoded.Bytes,previous.ReadFrame(index));
                if(identical==false)throw new InvalidDataException("Encoded frame differs from the approved reference: "+index);
                var item=new{frame=index,decodeMilliseconds=decode,encodeAndVerifyMilliseconds=total-decode,totalMilliseconds=total,encodedBytes=encoded.Bytes.Length,qualitySteps=encoded.Steps,coefficientRmsError=Math.Sqrt(encoded.SquaredError/encoded.CoefficientCount),identicalToReference=identical,sha256=Bytes.Hash(encoded.Bytes)};
                samples.Add(item);Console.WriteLine(Optimizer.Json(item));
            }
        }
    }
}
}
