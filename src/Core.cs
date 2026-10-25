using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Rawer {
public static class Bytes {
    public static uint U32(byte[] b, int p) { return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3]; }
    public static int U16(byte[] b, int p) { return (b[p]<<8)|b[p+1]; }
    public static long U64(byte[] b, int p) { ulong n=((ulong)U32(b,p)<<32)|U32(b,p+4); if(n>long.MaxValue) throw new InvalidDataException("Offset exceeds supported file size."); return (long)n; }
    public static int U24(byte[] b, int p) { return (b[p]<<16)|(b[p+1]<<8)|b[p+2]; }
    public static void P32(byte[] b,int p,long n) { if(n<0||n>uint.MaxValue) throw new InvalidDataException("32-bit size overflow."); for(int i=3;i>=0;i--){b[p+i]=(byte)n;n>>=8;} }
    public static void P64(byte[] b,int p,long n) { if(n<0)throw new InvalidDataException("Negative size.");for(int i=7;i>=0;i--){b[p+i]=(byte)n;n>>=8;} }
    public static void P24(byte[] b,int p,int n) { if(n<0||n>0xffffff)throw new InvalidDataException("Precinct too large.");b[p]=(byte)(n>>16);b[p+1]=(byte)(n>>8);b[p+2]=(byte)n; }
    public static string Four(byte[] b,int p) { return Encoding.ASCII.GetString(b,p,4); }
    public static byte[] Part(byte[] b,int p,int n) { byte[] a=new byte[n];Buffer.BlockCopy(b,p,a,0,n);return a; }
    public static byte[] Read(FileStream s,long p,int n) { if(n<0||p<0||p>s.Length-n)throw new InvalidDataException("Read outside the source file.");byte[] b=new byte[n];s.Position=p;int k=0;while(k<n){int r=s.Read(b,k,n-k);if(r==0)throw new EndOfStreamException();k+=r;}return b; }
    public static string Hash(byte[] b) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
    public static bool Equal(byte[] a,byte[] b) { if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true; }
}

public sealed class Box {
    public string Type; public long Offset,Size; public int Header;
    public byte[] Data; public List<Box> Children;
    static readonly HashSet<string> Containers=new HashSet<string>(new[]{"moov","trak","mdia","minf","stbl","edts","dinf","udta"});
    public Box Child(string type) { return Children==null?null:Children.FirstOrDefault(x=>x.Type==type); }
    public Box Required(string type) { var b=Child(type);if(b==null)throw new InvalidDataException("Missing "+type+" box.");return b; }
    public static List<Box> Parse(byte[] bytes,int start,int end,int depth) {
        if(depth>16)throw new InvalidDataException("Container nesting is too deep.");
        var result=new List<Box>();int p=start;
        while(p<end) {
            if(end-p<8)throw new InvalidDataException("Truncated box header.");
            long n=Bytes.U32(bytes,p);int h=8;if(n==1){if(end-p<16)throw new InvalidDataException("Truncated extended box.");n=Bytes.U64(bytes,p+8);h=16;}if(n==0)n=end-p;
            if(n<h||n>end-p)throw new InvalidDataException("Invalid box size.");
            var b=new Box{Type=Bytes.Four(bytes,p+4),Header=h,Size=n,Offset=p};
            if(Containers.Contains(b.Type))b.Children=Parse(bytes,p+h,p+(int)n,depth+1);
            else b.Data=Bytes.Part(bytes,p+h,(int)n-h);
            result.Add(b);p+=(int)n;
        }
        return result;
    }
    public byte[] Serialize() {
        byte[] data;
        if(Children!=null){using(var s=new MemoryStream()){foreach(var c in Children){var a=c.Serialize();s.Write(a,0,a.Length);}data=s.ToArray();}}
        else data=Data;
        byte[] b=new byte[Header+data.Length];Bytes.P32(b,0,Header==16?1:b.Length);Encoding.ASCII.GetBytes(Type).CopyTo(b,4);if(Header==16)Bytes.P64(b,8,b.Length);Buffer.BlockCopy(data,0,b,Header,data.Length);return b;
    }
    public IEnumerable<Box> Descendants() { yield return this;if(Children!=null)foreach(var c in Children)foreach(var b in c.Descendants())yield return b; }
}

public sealed class Chunk {
    public Track Track;public int Index,FirstSample,Count;public long Offset,Length;
}
public sealed class Track {
    public string Handler,Codec;public int Index,SampleCount,DefaultSize,Width,Height;public uint TimeScale;public long Duration;
    public int AudioChannels,AudioBits,AudioRate;public bool AudioFloat;
    public Box Node,Stsz,Offsets;public int[] Sizes;public long[] ChunkOffsets;public List<Chunk> Chunks=new List<Chunk>();
    public int SizeAt(int i) { return Sizes==null?DefaultSize:Sizes[i]; }
    public double Seconds { get{return (double)Duration/TimeScale;} }
    public object Summary() { return new {type=Handler,codec=Codec,samples=SampleCount,durationSeconds=Seconds,codedWidth=Width,codedHeight=Height,channels=AudioChannels,bits=AudioBits,sampleRate=AudioRate,floatAudio=AudioFloat}; }
}

public sealed class Movie : IDisposable {
    public string Path;public FileStream Stream;public long Length;public List<Box> Top=new List<Box>();public Box Moov,Mdat;public List<Track> Tracks=new List<Track>();
    public Track Video { get{return Tracks.Single(x=>x.Handler=="vide");} }
    public Movie(string path) {
        Path=System.IO.Path.GetFullPath(path);Stream=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try { Load(); } catch { Stream.Dispose();throw; }
    }
    void Load() {
        Length=Stream.Length;long p=0;
        while(p<Length){if(Length-p<8)throw new InvalidDataException("Truncated top-level box.");var h=Bytes.Read(Stream,p,(int)Math.Min(16,Length-p));long n=Bytes.U32(h,0);int hs=8;if(n==1){n=Bytes.U64(h,8);hs=16;}if(n==0)n=Length-p;if(n<hs||n>Length-p)throw new InvalidDataException("Invalid top-level box.");
            string type=Bytes.Four(h,4);var box=new Box{Type=type,Offset=p,Size=n,Header=hs};
            if(type=="moov"){if(n>32*1024*1024)throw new InvalidDataException("Movie metadata exceeds the prototype's limit.");box=Box.Parse(Bytes.Read(Stream,p,(int)n),0,(int)n,0).Single();box.Offset=p;}
            else if(type!="mdat"){if(n>16*1024*1024)throw new InvalidDataException("Unexpected non-media box size.");box.Data=Bytes.Read(Stream,p+hs,(int)n-hs);}
            Top.Add(box);p+=n;
        }
        if(Top.Count(x=>x.Type=="mdat")!=1||Top.Count(x=>x.Type=="moov")!=1)throw new NotSupportedException("Only unfragmented Nikon movies with one media-data box are supported.");
        if(Top.Any(x=>!new[]{"ftyp","moov","free","mdat"}.Contains(x.Type)))throw new NotSupportedException("This movie has an unsupported top-level box.");
        Moov=Top.Single(x=>x.Type=="moov");Mdat=Top.Single(x=>x.Type=="mdat");
        if(Moov.Descendants().Any(x=>new[]{"saio","senc","stz2","iloc","mvex"}.Contains(x.Type)))throw new NotSupportedException("Encrypted, fragmented or alternate sample tables are not supported.");
        foreach(var node in Moov.Children.Where(x=>x.Type=="trak")) {
            var md=node.Required("mdia");var hd=md.Required("hdlr").Data;var dh=md.Required("mdhd").Data;
            var t=new Track{Index=Tracks.Count,Node=node,Handler=Bytes.Four(hd,8)};
            int version=dh[0];if(version>1)throw new NotSupportedException("Unknown media-header version.");t.TimeScale=Bytes.U32(dh,version==1?20:12);t.Duration=version==1?Bytes.U64(dh,24):Bytes.U32(dh,16);if(t.TimeScale==0)throw new InvalidDataException("Zero time scale.");
            var st=md.Required("minf").Required("stbl");var sd=st.Required("stsd").Data;
            if(Bytes.U32(sd,4)!=1)throw new NotSupportedException("Multiple sample descriptions are not supported.");
            int entry=8,entrySize=(int)Bytes.U32(sd,entry);if(entrySize<8||entry+entrySize>sd.Length)throw new InvalidDataException("Invalid sample description.");t.Codec=Bytes.Four(sd,entry+4);
            if(t.Handler=="vide"){t.Width=Bytes.U16(sd,entry+32);t.Height=Bytes.U16(sd,entry+34);}
            if(t.Handler=="soun"&&Bytes.U16(sd,entry+16)==2){var rate=Bytes.Part(sd,entry+40,8);Array.Reverse(rate);t.AudioRate=(int)BitConverter.ToDouble(rate,0);t.AudioChannels=(int)Bytes.U32(sd,entry+48);t.AudioBits=(int)Bytes.U32(sd,entry+56);t.AudioFloat=(Bytes.U32(sd,entry+60)&1)!=0;}
            t.Stsz=st.Required("stsz");var sz=t.Stsz.Data;t.DefaultSize=checked((int)Bytes.U32(sz,4));t.SampleCount=checked((int)Bytes.U32(sz,8));if(t.SampleCount<1||t.SampleCount>100000000)throw new NotSupportedException("Sample count is outside the prototype's limit.");
            if(t.DefaultSize==0){if(t.SampleCount>1000000||12L+4L*t.SampleCount!=sz.Length)throw new InvalidDataException("Invalid variable sample table.");t.Sizes=new int[t.SampleCount];for(int i=0;i<t.SampleCount;i++)t.Sizes[i]=checked((int)Bytes.U32(sz,12+i*4));}
            t.Offsets=st.Child("co64")??st.Required("stco");var co=t.Offsets.Data;int count=checked((int)Bytes.U32(co,4)),stride=t.Offsets.Type=="co64"?8:4;if(count<1||count>1000000||8L+(long)stride*count!=co.Length)throw new InvalidDataException("Invalid chunk offsets.");
            t.ChunkOffsets=new long[count];for(int i=0;i<count;i++)t.ChunkOffsets[i]=stride==8?Bytes.U64(co,8+i*8):Bytes.U32(co,8+i*4);
            var sc=st.Required("stsc").Data;int entries=checked((int)Bytes.U32(sc,4));if(entries<1||8L+12L*entries!=sc.Length||Bytes.U32(sc,8)!=1)throw new InvalidDataException("Invalid sample-to-chunk map.");
            int map=0,sample=0;uint lastFirst=0;for(int i=0;i<entries;i++){uint f=Bytes.U32(sc,8+i*12);if(f<=lastFirst||f>count||Bytes.U32(sc,16+i*12)!=1)throw new InvalidDataException("Unsupported chunk-map entry.");lastFirst=f;}
            for(int i=0;i<count;i++){while(map+1<entries&&Bytes.U32(sc,8+(map+1)*12)<=i+1)map++;int c=checked((int)Bytes.U32(sc,12+map*12));if(c<1||c>t.SampleCount-sample)throw new InvalidDataException("Chunk exceeds sample table.");long len=t.DefaultSize==0?0:(long)t.DefaultSize*c;if(t.DefaultSize==0)for(int j=0;j<c;j++)len+=t.Sizes[sample+j];long at=t.ChunkOffsets[i];if(len<1||at<Mdat.Offset+Mdat.Header||at>Mdat.Offset+Mdat.Size-len)throw new InvalidDataException("Chunk outside media data.");t.Chunks.Add(new Chunk{Track=t,Index=i,FirstSample=sample,Count=c,Offset=at,Length=len});sample+=c;}
            if(sample!=t.SampleCount)throw new InvalidDataException("Unreferenced samples in table.");Tracks.Add(t);
        }
        if(Tracks.Count(x=>x.Handler=="vide")!=1||Video.Codec!="NR3D")throw new NotSupportedException("This prototype requires a Nikon R3D-NE (NR3D) movie.");
        if(Video.SampleCount>1000000||Video.Chunks.Any(x=>x.Count!=1))throw new NotSupportedException("The prototype requires one video frame per chunk.");
        long end=Mdat.Offset+Mdat.Header;foreach(var c in OrderedChunks()){if(c.Offset<end)throw new InvalidDataException("Overlapping media chunks.");end=c.Offset+c.Length;}
    }
    public IEnumerable<Chunk> OrderedChunks(){return Tracks.SelectMany(x=>x.Chunks).OrderBy(x=>x.Offset);}
    public byte[] ReadFrame(int index){var c=Video.Chunks[index];if(c.Length>64*1024*1024)throw new NotSupportedException("Video frame is too large.");return Bytes.Read(Stream,c.Offset,(int)c.Length);}
    public object Summary(){return new{file=System.IO.Path.GetFileName(Path),bytes=Length,tracks=Tracks.Select(x=>x.Summary()).ToArray()};}
    public void Dispose(){Stream.Dispose();}
}

public sealed class CodecResult { public byte[] Frame;public int Removed,Precincts,Packets; }
public static class NikonCodec {
    static void Need(bool condition,string message){if(!condition)throw new NotSupportedException(message);}
    public static CodecResult Compact(byte[] input) {
        Need(input.Length>=1200&&Bytes.Four(input,4)=="NRAW"&&Bytes.U32(input,0)==input.Length,"Unsupported Nikon frame wrapper.");
        int nrti=-1;
        for(int p=8;p<1024;){int n=checked((int)Bytes.U32(input,p));Need(n>=8&&p+n<=1024,"Invalid Nikon frame-prefix box.");if(Bytes.Four(input,p+4)=="NRTH")for(int q=p+8;q<p+n;){int z=checked((int)Bytes.U32(input,q));Need(z>=8&&q+z<=p+n,"Invalid Nikon frame data header.");if(Bytes.Four(input,q+4)=="NRTI"){Need(z==16&&Bytes.Four(input,q+8)=="0100","Unsupported Nikon image information.");nrti=q+12;}q+=z;}p+=n;}
        Need(nrti>=0&&Bytes.U32(input,nrti)==input.Length-1024,"Missing or inconsistent NRTI image size.");
        Need(Bytes.U16(input,1024)==0xff10,"JPEG XS start marker missing.");
        int pih=-1,firstSlice=-1,bandCount=0,components=0,width=0,height=0,sliceRows=0;
        for(int p=1026;p<input.Length;){Need(p+4<=input.Length,"Truncated JPEG XS header.");int marker=Bytes.U16(input,p);if(marker==0xff20){firstSlice=p;break;}int n=Bytes.U16(input,p+2);Need(n>=2&&p+2+n<=input.Length,"Invalid JPEG XS marker length.");
            if(marker==0xff12){pih=p;Need(n==39,"Unsupported Nikon picture-header revision.");Need(Bytes.U32(input,p+4)==input.Length-1024,"Inconsistent picture codestream size.");width=Bytes.U16(input,p+12);height=Bytes.U16(input,p+14);sliceRows=Bytes.U16(input,p+18);components=input[p+20];Need(width==6060&&height==3410&&sliceRows==16&&components==4&&input[p+21]==4&&input[p+22]==8,"The prototype currently supports the tested Nikon ZR 6K profile only.");Need(Bytes.Four(input,p+24)=="D\u0010Q\u0014","Unsupported Nikon coding parameters.");}
            else if(marker==0xff13){Need(n==14&&BitConverter.ToString(Bytes.Part(input,p+4,12))=="0E-11-51-0E-11-51-0E-11-00-0E-11-51","Unsupported Nikon component layout.");}
            else if(marker==0xff14){Need((n-2)%2==0,"Invalid weight table.");bandCount=(n-2)/2;}
            else Need(marker==0xff50,"Unknown Nikon codestream header marker.");p+=n+2;
        }
        Need(pih>=0&&firstSlice>0&&bandCount==25,"Unsupported Nikon codestream headers.");
        int precinctHeader=5+(bandCount*2+7)/8,expected=(height+3)/4,packetCount=8;
        int significanceBytes=((width/2+3)/4+7)/8;significanceBytes=(significanceBytes+7)/8;
        int precincts=0,packets=0,slice=-1,pointer=firstSlice,removed=0;
        using(var output=new MemoryStream(input.Length)){
            output.Write(input,0,firstSlice);
            while(pointer<input.Length-2){
                if(precincts%sliceRows==0){Need(pointer+6<=input.Length&&Bytes.U16(input,pointer)==0xff20&&Bytes.U16(input,pointer+2)==4,"Missing or invalid slice header.");slice++;Need(Bytes.U16(input,pointer+4)==slice,"Nonsequential slice index.");output.Write(input,pointer,6);pointer+=6;}
                Need(pointer+precinctHeader<=input.Length-2,"Truncated precinct header.");int declared=Bytes.U24(input,pointer),body=pointer+precinctHeader,end=body+declared;Need(declared>0&&end<=input.Length-2,"Invalid precinct size.");
                for(int i=0;i<6;i++)Need(input[pointer+5+i]==255,"Unsupported Nikon coding mode.");Need(input[pointer+11]==192,"Unsupported Nikon coding mode padding.");
                int packetEnd=body;
                for(int packet=0;packet<packetCount;packet++){
                    Need(packetEnd+7<=end,"Truncated packet header.");int q=packetEnd,raw=input[q]>>7;
                    int data=((input[q]&127)<<13)|(input[q+1]<<5)|(input[q+2]>>3);
                    int gcli=((input[q+2]&7)<<17)|(input[q+3]<<9)|(input[q+4]<<1)|(input[q+5]>>7);
                    int signs=((input[q+5]&127)<<8)|input[q+6];
                    Need(raw==0,"Raw-mode packets are not supported in this prototype.");int used=7+significanceBytes+data+gcli+signs;Need(used<=end-packetEnd,"Packet size exceeds the precinct.");packetEnd+=used;packets++;
                }
                for(int q=packetEnd;q<end;q++)Need(input[q]==0,"The proposed padding contains data; frame is left unsupported.");
                byte[] header=Bytes.Part(input,pointer,precinctHeader);Bytes.P24(header,0,packetEnd-body);output.Write(header,0,header.Length);output.Write(input,body,packetEnd-body);removed+=end-packetEnd;pointer=end;precincts++;
                Need(precincts<=expected,"Too many precincts.");
            }
            Need(pointer==input.Length-2&&Bytes.U16(input,pointer)==0xff11&&precincts==expected,"Invalid end marker or unexpected precinct count.");
            output.Write(input,pointer,2);byte[] frame=output.ToArray();Bytes.P32(frame,0,frame.Length);Bytes.P32(frame,nrti,frame.Length-1024);Bytes.P32(frame,pih+4,frame.Length-1024);
            return new CodecResult{Frame=frame,Removed=removed,Precincts=precincts,Packets=packets};
        }
    }
}

public sealed class ProgressInfo { public int Frame,Frames;public long Removed;public string Stage; }
public static class Optimizer {
    public static string Json(object value){return new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(value);}
    static void Copy(FileStream source,FileStream dest,long at,long count,CancellationToken cancel){source.Position=at;byte[] buffer=new byte[1024*1024];while(count>0){cancel.ThrowIfCancellationRequested();int n=source.Read(buffer,0,(int)Math.Min(count,buffer.Length));if(n==0)throw new EndOfStreamException();dest.Write(buffer,0,n);count-=n;}}
    static void SetOffsets(Track t,long[] values){int step=t.Offsets.Type=="co64"?8:4;for(int i=0;i<values.Length;i++)if(step==8)Bytes.P64(t.Offsets.Data,8+i*8,values[i]);else Bytes.P32(t.Offsets.Data,8+i*4,values[i]);}
    static void SetSizes(Track video,int[] values){byte[] d=new byte[12+values.Length*4];Buffer.BlockCopy(video.Stsz.Data,0,d,0,4);Bytes.P32(d,8,values.Length);for(int i=0;i<values.Length;i++)Bytes.P32(d,12+i*4,values[i]);video.Stsz.Data=d;}
    static Dictionary<string,string> LeafHashes(Movie m){var result=new Dictionary<string,string>();Action<Box,string> walk=null;walk=(box,key)=>{if(box.Children==null){if(box.Type!="stsz"&&box.Type!="stco"&&box.Type!="co64")result[key]=Bytes.Hash(box.Data);return;}for(int i=0;i<box.Children.Count;i++)walk(box.Children[i],key+"/"+i+":"+box.Children[i].Type);};walk(m.Moov,"moov");return result;}
    public static object Compact(string input,string output,double targetRatio,Action<ProgressInfo> progress,CancellationToken cancel) { return Process(input,output,targetRatio,progress,cancel,false); }
    public static object Reencode(string input,string output,double targetRatio,Action<ProgressInfo> progress,CancellationToken cancel,int workers=0) { return Process(input,output,targetRatio,progress,cancel,true,workers); }
    static object Process(string input,string output,double targetRatio,Action<ProgressInfo> progress,CancellationToken cancel,bool reencode,int workers=0) {
        input=Path.GetFullPath(input);output=Path.GetFullPath(output);
        if(string.Equals(input,output,StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a separate output file.");
        if(!string.Equals(Path.GetExtension(output),".r3d",StringComparison.OrdinalIgnoreCase))throw new IOException("The output must use the .R3D extension.");
        string reportPath=output+".report.json";if(File.Exists(output)||File.Exists(reportPath))throw new IOException("The output or its report already exists. Choose another filename.");
        if(targetRatio<1||double.IsNaN(targetRatio)||double.IsInfinity(targetRatio))throw new ArgumentOutOfRangeException("targetRatio");
        Directory.CreateDirectory(Path.GetDirectoryName(output));string partial=output+".partial-"+Guid.NewGuid().ToString("N");bool created=false,published=false;
        DateTime started=DateTime.UtcNow;
        try {
            using(var source=new Movie(input)) {
                var video=source.Video;var inputSummary=source.Summary();int[] frameSizes=new int[video.SampleCount];var offsets=source.Tracks.ToDictionary(t=>t.Index,t=>new long[t.Chunks.Count]);
                long nonVideo=source.Length-video.Chunks.Sum(c=>c.Length);long available=(long)Math.Floor(source.Length/targetRatio)-nonVideo;int budget=0;
                if(reencode){if(available/video.SampleCount<16384||available/video.SampleCount>int.MaxValue)throw new NotSupportedException("Requested file size is outside the supported range.");budget=(int)(available/video.SampleCount);}
                int[] qualitySteps=reencode?new int[video.SampleCount]:null;string[] encodedHashes=reencode?new string[video.SampleCount]:null;double coefficientSquaredError=0;long coefficientCount=0;int maximumCoefficientError=0;int encoderWorkers=0;
                SetSizes(video,frameSizes);byte[] placeholder=source.Moov.Serialize();var ftyp=source.Top.Single(x=>x.Type=="ftyp").Serialize();long mediaStart=Math.Max(source.Mdat.Offset,((ftyp.Length+(long)placeholder.Length+8+4095)/4096)*4096);
                long gapSize=mediaStart-ftyp.Length-placeholder.Length;if(gapSize<8||gapSize>uint.MaxValue)throw new NotSupportedException("Header reservation is outside the prototype's limit.");
                long removed=0;
                using(var dest=new FileStream(partial,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,1024*1024,FileOptions.SequentialScan)) {
                    using(var pipeline=reencode?new FramePipeline(source,budget,cancel,workers):null){
                    if(pipeline!=null)encoderWorkers=pipeline.Workers;
                    created=true;dest.Write(ftyp,0,ftyp.Length);dest.Write(placeholder,0,placeholder.Length);byte[] free=new byte[8];Bytes.P32(free,0,gapSize);Encoding.ASCII.GetBytes("free").CopyTo(free,4);dest.Write(free,0,8);dest.SetLength(mediaStart);dest.Position=mediaStart;byte[] mh=new byte[source.Mdat.Header];Bytes.P32(mh,0,mh.Length==16?1:8);Encoding.ASCII.GetBytes("mdat").CopyTo(mh,4);dest.Write(mh,0,mh.Length);
                    long cursor=source.Mdat.Offset+source.Mdat.Header;
                    foreach(var chunk in source.OrderedChunks()) {
                        cancel.ThrowIfCancellationRequested();Copy(source.Stream,dest,cursor,chunk.Offset-cursor,cancel);offsets[chunk.Track.Index][chunk.Index]=dest.Position;
                        if(chunk.Track==video){
                            int index=chunk.FirstSample;byte[] written;
                            if(reencode){var encoded=pipeline.Take(index);written=encoded.Bytes;qualitySteps[index]=encoded.Steps;encodedHashes[index]=Bytes.Hash(written);coefficientSquaredError+=encoded.SquaredError;coefficientCount+=encoded.CoefficientCount;maximumCoefficientError=Math.Max(maximumCoefficientError,encoded.MaximumError);}
                            else written=NikonCodec.Compact(source.ReadFrame(index)).Frame;
                            dest.Write(written,0,written.Length);frameSizes[index]=written.Length;removed+=chunk.Length-written.Length;
                            if(progress!=null)progress(new ProgressInfo{Frame=index+1,Frames=video.SampleCount,Removed=removed,Stage=reencode?"Повторное кодирование RAW":"Сжатие копии"});
                        }
                        else Copy(source.Stream,dest,chunk.Offset,chunk.Length,cancel);
                        cursor=chunk.Offset+chunk.Length;
                    }
                    Copy(source.Stream,dest,cursor,source.Mdat.Offset+source.Mdat.Size-cursor,cancel);long length=dest.Length;
                    if(mh.Length==16)Bytes.P64(mh,8,length-mediaStart);else Bytes.P32(mh,0,length-mediaStart);
                    dest.Position=mediaStart;dest.Write(mh,0,mh.Length);foreach(var t in source.Tracks)SetOffsets(t,offsets[t.Index]);SetSizes(video,frameSizes);var final=source.Moov.Serialize();if(final.Length!=placeholder.Length)throw new InvalidDataException("Metadata reservation changed unexpectedly.");dest.Position=ftyp.Length;dest.Write(final,0,final.Length);dest.Flush(true);
                    }
                }
                // Reopen both files: verification must use bytes actually saved to disk.
                object verification=Verify(input,partial,progress,cancel,encodedHashes);
                long outputBytes=new FileInfo(partial).Length;double ratio=(double)source.Length/outputBytes;
                object quantization=reencode?(object)new{domain="Inverse-quantized wavelet coefficients before inverse wavelet and Bayer reconstruction; these are not image PSNR/SSIM measurements",coefficientCount=coefficientCount,rmsCoefficientError=Math.Sqrt(coefficientSquaredError/coefficientCount),maximumCoefficientError=maximumCoefficientError,qualityStepsPerFrame=qualitySteps,perFrameByteBudget=budget}:null;
                var report=new {prototype="RAWER 0.6",encoderWorkers=encoderWorkers,mode=reencode?"nikon-raw-coefficient-requantization-and-entropy-reencoding":"lossless-precinct-padding-compaction",source=Path.GetFileName(input),destination=Path.GetFileName(output),sourceBytes=source.Length,outputBytes=outputBytes,removedBytes=source.Length-outputBytes,savingPercent=100.0*(1-(double)outputBytes/source.Length),compressionRatio=ratio,requestedRatio=targetRatio,targetMet=ratio>=targetRatio,startedUtc=started.ToString("o"),finishedUtc=DateTime.UtcNow.ToString("o"),input=inputSummary,verification=verification,quantization=quantization,visualDecodeVerified=false,qualityStatement=reencode?"Lossy RAW coefficient requantization. Every encoded frame was decoded by the prototype and matched its expected quantized coefficients. Independent editor decoding and visual quality are not yet verified.":"Coded image packets and RAW metadata are preserved; independent editor decoding is still required.",limitation="Experimental implementation validated on the supplied Nikon ZR profile. Independent decoder compatibility and visual quality require DaVinci Resolve verification."};
                cancel.ThrowIfCancellationRequested();if(File.Exists(output)||File.Exists(reportPath))throw new IOException("Output appeared while processing; publication cancelled.");
                File.Move(partial,output);published=true;
                using(var file=new FileStream(reportPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read))using(var writer=new StreamWriter(file,new UTF8Encoding(false)))writer.Write(Json(report));
                return report;
            }
        } finally { if(created&&!published&&File.Exists(partial))File.Delete(partial); }
    }
    public static object Verify(string original,string output,Action<ProgressInfo> progress,CancellationToken cancel,string[] encodedHashes=null) {
        using(var a=new Movie(original))using(var b=new Movie(output)) {
            if(a.Tracks.Count!=b.Tracks.Count)throw new InvalidDataException("Track count changed.");
            var ah=LeafHashes(a);var bh=LeafHashes(b);if(ah.Count!=bh.Count||ah.Any(x=>!bh.ContainsKey(x.Key)||bh[x.Key]!=x.Value))throw new InvalidDataException("Movie metadata changed outside the size and offset tables.");
            var hashes=new List<object>();
            for(int t=0;t<a.Tracks.Count;t++) {
                var at=a.Tracks[t];var bt=b.Tracks[t];if(at.Handler!=bt.Handler||at.Codec!=bt.Codec||at.SampleCount!=bt.SampleCount||at.Duration!=bt.Duration||at.TimeScale!=bt.TimeScale)throw new InvalidDataException("Track timing or format changed.");
                if(at.Handler=="vide") {
                    using(var sha=SHA256.Create()) {
                        for(int i=0;i<at.SampleCount;i++){
                            cancel.ThrowIfCancellationRequested();var saved=b.ReadFrame(i);var originalFrame=a.ReadFrame(i);
                            if(encodedHashes==null){if(!Bytes.Equal(NikonCodec.Compact(originalFrame).Frame,saved))throw new InvalidDataException("Frame "+i+" differs from the losslessly compacted source.");}
                            else {
                                if(Bytes.Hash(saved)!=encodedHashes[i])throw new InvalidDataException("Saved encoded frame hash differs from the verified encoder output.");
                                var oh=RawEntropy.Header(originalFrame);var sh=RawEntropy.Header(saved);if(oh.FirstSlice!=sh.FirstSlice)throw new InvalidDataException("RAW header length changed.");var op=Bytes.Part(originalFrame,0,oh.FirstSlice);var sp=Bytes.Part(saved,0,sh.FirstSlice);
                                foreach(int p in new[]{0,oh.Nrti,oh.Pih+4}){Bytes.P32(op,p,0);Bytes.P32(sp,p,0);}if(!Bytes.Equal(op,sp))throw new InvalidDataException("RAW camera metadata or raster changed.");
                            }
                            var stable=NikonCodec.Compact(saved).Frame;if(!Bytes.Equal(saved,stable))throw new InvalidDataException("Unexpected residual frame padding.");sha.TransformBlock(saved,0,saved.Length,null,0);if(progress!=null)progress(new ProgressInfo{Frame=i+1,Frames=at.SampleCount,Stage="Проверка записанного файла"});
                        }
                        sha.TransformFinalBlock(new byte[0],0,0);hashes.Add(new{track=at.Handler,frames=at.SampleCount,encodedFramesSha256=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant(),rawCameraMetadataPreserved=true,codedImagePacketsPreserved=encodedHashes==null,reencodedCoefficientsVerifiedBeforeWriting=encodedHashes!=null});
                    }
                } else {
                    if(at.Chunks.Count!=bt.Chunks.Count)throw new InvalidDataException("Non-video chunk count changed.");
                    using(var sha=SHA256.Create()) {
                        for(int i=0;i<at.Chunks.Count;i++){cancel.ThrowIfCancellationRequested();var ac=at.Chunks[i];var bc=bt.Chunks[i];if(ac.Length!=bc.Length||ac.Count!=bc.Count)throw new InvalidDataException("Non-video chunk layout changed.");long p=0;while(p<ac.Length){int n=(int)Math.Min(1024*1024,ac.Length-p);var left=Bytes.Read(a.Stream,ac.Offset+p,n);var right=Bytes.Read(b.Stream,bc.Offset+p,n);if(!Bytes.Equal(left,right))throw new InvalidDataException("Audio or timecode payload changed.");sha.TransformBlock(right,0,right.Length,null,0);p+=n;}}
                        sha.TransformFinalBlock(new byte[0],0,0);hashes.Add(new{track=at.Handler,bytesPreserved=true,sha256=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant()});
                    }
                }
            }
            return new{allFramesChecked=a.Video.SampleCount,metadataUnchangedExceptRequiredSizeAndOffsetTables=true,trackResults=hashes.ToArray(),independentImageDecodePerformed=false};
        }
    }
}
}
