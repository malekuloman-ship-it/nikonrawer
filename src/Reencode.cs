// SPDX-License-Identifier: LGPL-2.1
// Nikon entropy decoding and layout adapted from nbuchwitz/dnglab,
// branch nikon-he-jpegxs, rawler/src/decompressors/jpegxs/{pi,entropy}.rs.
// See third_party/dnglab-reference.json for the exact upstream file revisions.
// The entropy encoder and coefficient-domain requantizer are new RAWER code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Rawer {
internal sealed class BitInput {
    readonly byte[] data;readonly int end;public int Pos,Bits;
    public BitInput(byte[] data,int start,int end){this.data=data;Pos=start;this.end=end;}
    public int Bit(){if(Pos>=end)throw new InvalidDataException("Entropy bitstream is truncated.");int v=(data[Pos]>>(7-Bits))&1;if(++Bits==8){Bits=0;Pos++;}return v;}
    public int Nibble(){if(Pos>=end)throw new InvalidDataException("Coefficient bitstream is truncated.");if(Bits==0){Bits=4;return data[Pos]>>4;}if(Bits==4){Bits=0;return data[Pos++]&15;}throw new InvalidDataException("Unaligned coefficient nibble.");}
    public int Unary(){int n=0;while(Bit()!=0)if(++n>=32)throw new InvalidDataException("Invalid unary code.");return n;}
    public void Align(){if(Bits!=0){Bits=0;Pos++;}}
    public void Finish(int start,int size){Align();if(Pos>start+size||start+size>end)throw new InvalidDataException("Decoded entropy section exceeds its declared size.");Pos=start+size;}
}
internal sealed class BitOutput {
    byte[] data=new byte[1024];int count,bits;
    void Reserve(){if(count>=data.Length)Array.Resize(ref data,data.Length*2);}
    public void Bit(int v){Reserve();if(v!=0)data[count]|=(byte)(1<<(7-bits));if(++bits==8){bits=0;count++;}}
    public void Nibble(int v){if(bits==0){Reserve();data[count]=(byte)(v<<4);bits=4;}else if(bits==4){data[count]|=(byte)v;bits=0;count++;}else throw new InvalidDataException("Unaligned encoded nibble.");}
    public void Unary(int v){if(v<0||v>31)throw new InvalidDataException("Encoded unary value is out of range.");for(int i=0;i<v;i++)Bit(1);Bit(0);}
    public byte[] Finish(){if(bits!=0){bits=0;count++;}return Bytes.Part(data,0,count);}
    public byte[] Buffer {get{return data;}}
    public int Seal(){if(bits!=0){bits=0;count++;}return count;}
    public void Reset(){Array.Clear(data,0,count+(bits==0?0:1));count=bits=0;}
}
internal sealed class PacketWriters {
    public readonly BitOutput Significance=new BitOutput(),Gcli=new BitOutput(),Data=new BitOutput(),Sign=new BitOutput();public readonly byte[] Header=new byte[7];
    public void Reset(){Significance.Reset();Gcli.Reset();Data.Reset();Sign.Reset();}
}
internal sealed class WaveBand {
    public int Width,Groups,Significance,Lines,Gain,Priority,Component,Local;
    public int Stride { get{return Groups*4;} }
    public int Truncation(int qp,int rp){return Math.Min(15,Math.Max(0,qp-Gain-(Priority<rp?1:0)));}
}
internal sealed class WavePacket {public int First,End,Line;}
internal sealed class BandState {public int T;public byte[] G;public ushort[] C;}
internal sealed class PrecinctState {public int Q,R;public BandState[] Bands;}
internal sealed class FrameState {
    public byte[] Source;public int FirstSlice,Pih,Nrti,Count,SliceHeight,Width,Height;
    public WaveBand[] Bands;public WavePacket[] Packets;public PrecinctState[] Precincts;
}
internal sealed class EncodedRawFrame {
    public byte[] Bytes;public int Steps;public long CoefficientCount;public double SquaredError;public int MaximumError;
    public PrecinctState[] Expected;
}
internal sealed class QuantizationMap {public ushort[] Codes,Errors;}
internal static class RawEntropy {
    // The transform uses only 16 coefficient ranges and truncation levels.
    // Cache the exact original rounding calculation instead of repeating it
    // millions of times per frame. Published maps are immutable and thread safe.
    static readonly QuantizationMap[] quantizationMaps=new QuantizationMap[4096];
    static QuantizationMap Quantization(int g,int oldT,int nextT){
        int key=(g<<8)|(oldT<<4)|nextT;var saved=Volatile.Read(ref quantizationMaps[key]);if(saved!=null)return saved;
        int length=1<<Math.Max(0,g-oldT);var map=new QuantizationMap{Codes=new ushort[length],Errors=new ushort[length]};
        for(int i=1;i<length;i++){
            int mag=i<<oldT,oldValue=InverseMagnitude(mag,g,oldT),code=0;
            if(g>nextT){
                int planes=g-nextT,scale=planes+1,maximum=(1<<planes)-1;
                double estimate=oldValue*(1.0-1.0/(1<<scale))/(1<<nextT);
                int q=Math.Max(0,Math.Min(maximum,(int)Math.Round(estimate))),best=q,bestError=int.MaxValue;
                for(int candidate=Math.Max(0,q-1);candidate<=Math.Min(maximum,q+1);candidate++){
                    int restored=InverseMagnitude(candidate<<nextT,g,nextT),error=Math.Abs(oldValue-restored);if(error<bestError){best=candidate;bestError=error;}
                }
                code=best<<nextT;
            }
            map.Codes[i]=(ushort)code;map.Errors[i]=(ushort)Math.Abs(oldValue-InverseMagnitude(code,g,nextT));
        }
        return Interlocked.CompareExchange(ref quantizationMaps[key],map,null)??map;
    }
    static void Require(bool ok,string message){if(!ok)throw new NotSupportedException(message);}
    public static FrameState Header(byte[] frame){
        Require(frame.Length>1200&&Bytes.Four(frame,4)=="NRAW"&&Bytes.U32(frame,0)==frame.Length,"Unsupported Nikon frame wrapper.");
        var f=new FrameState{Source=frame,Nrti=-1,Pih=-1};byte[] weights=null,cdt=null;
        for(int p=8;p<1024;){int n=checked((int)Bytes.U32(frame,p));Require(n>=8&&p+n<=1024,"Invalid Nikon prefix.");if(Bytes.Four(frame,p+4)=="NRTH")for(int q=p+8;q<p+n;){int z=checked((int)Bytes.U32(frame,q));Require(z>=8&&q+z<=p+n,"Invalid Nikon image header.");if(Bytes.Four(frame,q+4)=="NRTI"){Require(z==16,"Unsupported NRTI.");f.Nrti=q+12;}q+=z;}p+=n;}
        Require(f.Nrti>=0&&Bytes.U32(frame,f.Nrti)==frame.Length-1024,"Inconsistent Nikon codestream size.");Require(Bytes.U16(frame,1024)==0xff10,"Missing SOC.");
        int at=1026;
        while(at<frame.Length-2){int m=Bytes.U16(frame,at);if(m==0xff20){f.FirstSlice=at;break;}int n=Bytes.U16(frame,at+2);Require(n>=2&&at+n+2<=frame.Length,"Invalid codestream header.");
            if(m==0xff12){f.Pih=at;Require(n==39&&Bytes.U32(frame,at+4)==frame.Length-1024,"Unsupported Nikon PIH.");f.Width=Bytes.U16(frame,at+12);f.Height=Bytes.U16(frame,at+14);f.SliceHeight=Bytes.U16(frame,at+18);Require(f.Width==6060&&f.Height==3410&&f.SliceHeight==16&&frame[at+20]==4&&frame[at+21]==4&&frame[at+22]==8,"Only the tested Nikon ZR 6K profile is supported.");Require(frame[at+24]==0x44&&frame[at+25]==0x10&&frame[at+26]==0x51&&frame[at+27]==0x14,"Unsupported precision, progression, quantizer or sign mode.");}
            else if(m==0xff13)cdt=Bytes.Part(frame,at+4,n-2);
            else if(m==0xff14)weights=Bytes.Part(frame,at+4,n-2);
            else Require(m==0xff50,"Unknown codestream header marker.");at+=n+2;
        }
        Require(f.Pih>0&&f.FirstSlice>0&&cdt!=null&&cdt.Length==12&&weights!=null&&weights.Length==50,"Incomplete Nikon headers.");
        var bands=new List<WaveBand>();int[] starts=new int[4],ends=new int[4];int w=f.Width/2;
        for(int c=0;c<4;c++){
            starts[c]=bands.Count;Require(cdt[c*3]==14&&cdt[c*3+1]==17,"Unsupported component precision or subsampling.");int transform=cdt[c*3+2];Require(transform==(c==2?0:0x51),"Unsupported component wavelet layout.");var widths=new List<int>();
            if(transform==0)widths.Add(w);else {int lo=w;var highs=new List<int>();for(int k=0;k<5;k++){int hi=lo/2;highs.Add(hi);lo-=hi;}widths.Add(lo);highs.Reverse();widths.AddRange(highs);widths.Add(w-w/2);widths.Add(w/2);}
            for(int k=0;k<widths.Count;k++){int groups=(widths[k]+3)/4;bands.Add(new WaveBand{Component=c,Local=k,Width=widths[k],Groups=groups,Significance=(groups+7)/8,Lines=c==2?2:1});}ends[c]=bands.Count;
        }
        var packets=new List<WavePacket>();for(int half=0;half<2;half++)for(int c=0;c<4;c++)packets.Add(new WavePacket{First=starts[c]+(c==2||half==0?0:6),End=c==2?ends[c]:(half==0?starts[c]+6:ends[c]),Line=half});
        bool[] assigned=new bool[bands.Count];int wi=0;foreach(var packet in packets)for(int i=packet.First;i<packet.End;i++)if(!assigned[i]){assigned[i]=true;bands[i].Gain=weights[wi++];bands[i].Priority=weights[wi++];}
        Require(bands.Count==25&&wi==weights.Length,"Invalid Nikon band weight layout.");f.Bands=bands.ToArray();f.Packets=packets.ToArray();f.Count=(f.Height/2+1)/2;return f;
    }
    public static PrecinctState Empty(FrameState f,int qp,int rp){return new PrecinctState{Q=qp,R=rp,Bands=f.Bands.Select(b=>new BandState{T=b.Truncation(qp,rp),G=new byte[b.Groups*b.Lines],C=new ushort[b.Stride*b.Lines]}).ToArray()};}
    public static FrameState Decode(byte[] frame,CancellationToken cancellation=default(CancellationToken)){
        var f=Header(frame);f.Precincts=new PrecinctState[f.Count];int at=f.FirstSlice;
        for(int index=0;index<f.Count;index++){
            if(index%f.SliceHeight==0)cancellation.ThrowIfCancellationRequested();
            if(index%f.SliceHeight==0){Require(Bytes.U16(frame,at)==0xff20&&Bytes.U16(frame,at+2)==4&&Bytes.U16(frame,at+4)==index/f.SliceHeight,"Invalid slice index.");at+=6;}
            int length=Bytes.U24(frame,at),end=at+12+length;Require(length>0&&end<=frame.Length-2,"Invalid precinct size.");int qp=frame[at+3],rp=frame[at+4];var current=Empty(f,qp,rp);var top=index%f.SliceHeight==0?null:f.Precincts[index-1];int[] modes=new int[25];for(int i=0;i<25;i++)modes[i]=(frame[at+5+i/4]>>(6-2*(i%4)))&3;
            var reader=new BitInput(frame,at+12,end);
            foreach(var packet in f.Packets){
                int p=reader.Pos;Require(p+7<=end,"Truncated packet header.");int raw=frame[p]>>7;int data=((frame[p]&127)<<13)|(frame[p+1]<<5)|(frame[p+2]>>3);int gcli=((frame[p+2]&7)<<17)|(frame[p+3]<<9)|(frame[p+4]<<1)|(frame[p+5]>>7);int signs=((frame[p+5]&127)<<8)|frame[p+6];reader.Pos+=7;
                var significant=new int[packet.End-packet.First][];
                if(raw==0){for(int id=packet.First;id<packet.End;id++)if((modes[id]&2)!=0){int n=f.Bands[id].Significance;var flags=new int[n];for(int j=0;j<n;j++)flags[j]=reader.Bit();significant[id-packet.First]=flags;}reader.Align();}
                int start=reader.Pos;
                for(int id=packet.First;id<packet.End;id++){
                    var band=f.Bands[id];var state=current.Bands[id];int line=band.Lines==2?packet.Line:0,row=line*band.Groups;int[] flags=significant[id-packet.First];BandState reference=null;int refRow=0,threshold=state.T;
                    if((modes[id]&1)!=0){if(line>0){reference=state;refRow=(line-1)*band.Groups;}else if(top!=null){reference=top.Bands[id];refRow=(band.Lines-1)*band.Groups;}if(reference!=null)threshold=Math.Max(state.T,reference.T);}
                    for(int group=0;group<band.Groups;group++){
                        int value;if(raw!=0)value=reader.Nibble();else {bool code=flags==null||flags[group/8]==0;if(reference==null)value=code?state.T+reader.Unary():state.T;else {int prediction=Math.Max(reference.G[refRow+group],threshold);if(!code)value=prediction;else {int unary=reader.Unary(),room=prediction-state.T,delta=unary>2*room?unary-room:((unary&1)!=0?-(unary+1)/2:unary/2);value=prediction+delta;}}}
                        Require(value>=0&&value<=15,"Decoded GCLI exceeds the supported coefficient range.");state.G[row+group]=(byte)value;
                    }
                }
                reader.Finish(start,gcli);start=reader.Pos;
                for(int id=packet.First;id<packet.End;id++){
                    var band=f.Bands[id];var state=current.Bands[id];int line=band.Lines==2?packet.Line:0,row=line*band.Groups,coeff=line*band.Stride;
                    for(int group=0;group<band.Groups;group++){
                        int planes=Math.Max(0,state.G[row+group]-state.T),a=0,b=0,c=0,d=0;
                        for(int bit=0;bit<planes;bit++){int v=reader.Nibble();a=(a<<1)|(v>>3);b=(b<<1)|((v>>2)&1);c=(c<<1)|((v>>1)&1);d=(d<<1)|(v&1);}
                        int k=coeff+group*4;state.C[k]=(ushort)(a<<state.T);state.C[k+1]=(ushort)(b<<state.T);state.C[k+2]=(ushort)(c<<state.T);state.C[k+3]=(ushort)(d<<state.T);
                    }
                }
                reader.Finish(start,data);start=reader.Pos;
                for(int id=packet.First;id<packet.End;id++){
                    var band=f.Bands[id];var state=current.Bands[id];int line=band.Lines==2?packet.Line:0,row=line*band.Stride;
                    for(int k=0;k<band.Stride;k++)if(state.C[row+k]!=0)state.C[row+k]|=(ushort)(reader.Bit()<<15);
                }
                reader.Finish(start,signs);
            }
            // An encoder may reserve zero-filled space after the significant packets.
            for(int p=reader.Pos;p<end;p++)Require(frame[p]==0,"Unexpected data after decoded packets.");f.Precincts[index]=current;at=end;
        }
        Require(at==frame.Length-2&&Bytes.U16(frame,at)==0xff11,"Missing codestream end marker.");return f;
    }
    public static int InverseMagnitude(int magnitude,int gcli,int gtli){if(magnitude==0||gtli==0||gcli<=gtli)return magnitude;int shift=gcli-gtli+1,result=0;while(magnitude>0){result+=magnitude;magnitude>>=shift;}return result;}
    static PrecinctState Quantize(FrameState f,PrecinctState source,int steps,EncodedRawFrame result){
        int rank=source.Q*25-source.R+steps;int qp=steps==0?source.Q:(rank+24)/25,rp=steps==0?source.R:qp*25-rank;var output=Empty(f,qp,rp);
        for(int id=0;id<f.Bands.Length;id++){
            var band=f.Bands[id];var old=source.Bands[id];var next=output.Bands[id];Require(next.T>=old.T,"A requantizer cannot recover discarded precision.");
            if(result!=null)result.CoefficientCount+=(long)band.Width*band.Lines;
            if(next.T==old.T){Array.Copy(old.C,next.C,old.C.Length);for(int j=0;j<old.G.Length;j++)next.G[j]=(byte)Math.Max(old.G[j],next.T);continue;}
            for(int line=0;line<band.Lines;line++)for(int group=0;group<band.Groups;group++){
                int gi=line*band.Groups+group,g=old.G[gi];next.G[gi]=(byte)Math.Max(g,next.T);int baseIndex=line*band.Stride+group*4;
                var map=Quantization(g,old.T,next.T);var codes=map.Codes;var errors=map.Errors;
                for(int k=0;k<4;k++){
                    int i=baseIndex+k,v=old.C[i],level=(v&32767)>>old.T,code=codes[level];next.C[i]=(ushort)(code==0?0:code|(v&32768));
                    if(result!=null&&group*4+k<band.Width){int error=errors[level];result.SquaredError+=(double)error*error;if(error>result.MaximumError)result.MaximumError=error;}
                }
            }
        }
        return output;
    }
    static int Prediction(FrameState f,PrecinctState current,PrecinctState top,int id,int line,int group){
        var band=f.Bands[id];var state=current.Bands[id];if(line>0)return Math.Max(state.G[(line-1)*band.Groups+group],state.T);
        if(top==null)return state.T;var previous=top.Bands[id];return Math.Max(previous.G[(band.Lines-1)*band.Groups+group],Math.Max(previous.T,state.T));
    }
    static void EncodePacket(FrameState f,PrecinctState current,PrecinctState top,WavePacket packet,PacketWriters writers,MemoryStream output){
        writers.Reset();var significance=writers.Significance;var gcli=writers.Gcli;var data=writers.Data;var sign=writers.Sign;
        for(int id=packet.First;id<packet.End;id++){
            var band=f.Bands[id];var state=current.Bands[id];int line=band.Lines==2?packet.Line:0,row=line*band.Groups;
            bool reference=line>0||top!=null;
            for(int s=0;s<band.Significance;s++){
                int begin=s*8,end=Math.Min(begin+8,band.Groups);bool skip=true;
                for(int group=begin;group<end;group++)if(state.G[row+group]!=Prediction(f,current,top,id,line,group)){skip=false;break;}
                significance.Bit(skip?1:0);
                if(!skip)for(int group=begin;group<end;group++){
                    int value=state.G[row+group],prediction=Prediction(f,current,top,id,line,group),unary;
                    if(!reference)unary=value-state.T;
                    else {int delta=value-prediction,room=prediction-state.T;unary=delta<0?-2*delta-1:(delta<=room?2*delta:delta+room);}
                    gcli.Unary(unary);
                }
            }
        }
        for(int id=packet.First;id<packet.End;id++){
            var band=f.Bands[id];var state=current.Bands[id];int line=band.Lines==2?packet.Line:0,row=line*band.Groups,baseIndex=line*band.Stride;
            for(int group=0;group<band.Groups;group++){
                int i=baseIndex+group*4,a=state.C[i]&32767,b=state.C[i+1]&32767,c=state.C[i+2]&32767,d=state.C[i+3]&32767;
                for(int bit=state.G[row+group]-1;bit>=state.T;bit--)data.Nibble((((a>>bit)&1)<<3)|(((b>>bit)&1)<<2)|(((c>>bit)&1)<<1)|((d>>bit)&1));
            }
            for(int k=0;k<band.Stride;k++){int v=state.C[baseIndex+k];if((v&32767)!=0)sign.Bit(v>>15);}
        }
        int sig=significance.Seal(),gl=gcli.Seal(),dl=data.Seal(),sl=sign.Seal();
        Require(dl<(1<<20)&&gl<(1<<20)&&sl<(1<<15),"Encoded packet size overflow.");
        var bytes=writers.Header;
        bytes[0]=(byte)(dl>>13);bytes[1]=(byte)(dl>>5);bytes[2]=(byte)(((dl&31)<<3)|(gl>>17));bytes[3]=(byte)(gl>>9);bytes[4]=(byte)(gl>>1);bytes[5]=(byte)(((gl&1)<<7)|(sl>>8));bytes[6]=(byte)sl;
        output.Write(bytes,0,7);output.Write(significance.Buffer,0,sig);output.Write(gcli.Buffer,0,gl);output.Write(data.Buffer,0,dl);output.Write(sign.Buffer,0,sl);
    }
    public static EncodedRawFrame Encode(FrameState f,int steps,bool keepExpected,CancellationToken cancellation=default(CancellationToken)){
        Require(steps>=0&&steps<=200,"Quality adjustment is outside the prototype's range.");var result=new EncodedRawFrame{Steps=steps,Expected=keepExpected?new PrecinctState[f.Count]:null};PrecinctState top=null;
        var writers=new PacketWriters();using(var output=new MemoryStream(f.Source.Length))using(var payload=new MemoryStream(32768)){
            output.Write(f.Source,0,f.FirstSlice);
            for(int index=0;index<f.Count;index++){
                if(index%f.SliceHeight==0)cancellation.ThrowIfCancellationRequested();
                if(index%f.SliceHeight==0){byte[] slice={255,32,0,4,(byte)((index/f.SliceHeight)>>8),(byte)(index/f.SliceHeight)};output.Write(slice,0,6);top=null;}
                var current=Quantize(f,f.Precincts[index],steps,result);payload.SetLength(0);payload.Position=0;
                foreach(var packet in f.Packets)EncodePacket(f,current,top,packet,writers,payload);
                byte[] h=new byte[12];Bytes.P24(h,0,(int)payload.Length);h[3]=(byte)current.Q;h[4]=(byte)current.R;for(int i=5;i<11;i++)h[i]=255;h[11]=192;output.Write(h,0,12);payload.WriteTo(output);
                if(keepExpected)result.Expected[index]=current;top=current;
            }
            output.WriteByte(255);output.WriteByte(17);result.Bytes=output.ToArray();Bytes.P32(result.Bytes,0,result.Bytes.Length);Bytes.P32(result.Bytes,f.Nrti,result.Bytes.Length-1024);Bytes.P32(result.Bytes,f.Pih+4,result.Bytes.Length-1024);
        }
        return result;
    }
    public static void VerifyEncoded(FrameState original,EncodedRawFrame encoded,CancellationToken cancellation=default(CancellationToken)){
        var decoded=Decode(encoded.Bytes,cancellation);
        Require(decoded.Count==original.Count&&decoded.Width==original.Width&&decoded.Height==original.Height,"Reencoded raster changed.");
        for(int p=0;p<decoded.Count;p++){
            if(p%original.SliceHeight==0)cancellation.ThrowIfCancellationRequested();
            var expected=encoded.Expected==null?Quantize(original,original.Precincts[p],encoded.Steps,null):encoded.Expected[p];
            for(int b=0;b<decoded.Bands.Length;b++){
                var a=expected.Bands[b];var v=decoded.Precincts[p].Bands[b];if(a.T!=v.T||!Equal(a.G,v.G)||!Equal(a.C,v.C))throw new NotSupportedException("Reencoded coefficients did not round-trip at precinct "+p+", band "+b+".");
            }
        }
    }
    static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static bool Equal(ushort[] a,ushort[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    // Exact byte count for this encoder's packets. It uses the same immutable
    // quantization lookup but writes no bitstream and allocates no coefficient
    // arrays. Packet sections are rounded separately, as in EncodePacket.
    internal static int EncodedSize(FrameState f,int steps,CancellationToken cancellation=default(CancellationToken)){
        Require(steps>=0&&steps<=200,"Quality adjustment is outside the prototype's range.");
        int bands=f.Bands.Length;var truncations=new byte[f.Count*bands];
        for(int p=0;p<f.Count;p++){
            var source=f.Precincts[p];int rank=source.Q*25-source.R+steps,qp=steps==0?source.Q:(rank+24)/25,rp=steps==0?source.R:qp*25-rank;
            for(int b=0;b<bands;b++){int t=f.Bands[b].Truncation(qp,rp);Require(t>=source.Bands[b].T,"A requantizer cannot recover discarded precision.");truncations[p*bands+b]=(byte)t;}
        }
        long bytes=f.FirstSlice+2L+6*((f.Count+f.SliceHeight-1)/f.SliceHeight)+12L*f.Count;
        for(int p=0;p<f.Count;p++){
            if(p%f.SliceHeight==0)cancellation.ThrowIfCancellationRequested();var current=f.Precincts[p];var top=p%f.SliceHeight==0?null:f.Precincts[p-1];
            foreach(var packet in f.Packets){
                int significanceBits=0,unaryBits=0,dataNibbles=0,signBits=0;
                for(int id=packet.First;id<packet.End;id++){
                    var band=f.Bands[id];var old=current.Bands[id];int t=truncations[p*bands+id],line=band.Lines==2?packet.Line:0,row=line*band.Groups;
                    BandState reference=line>0?old:(top==null?null:top.Bands[id]);int refRow=line>0?(line-1)*band.Groups:(band.Lines-1)*band.Groups;
                    int refT=line>0?t:(top==null?t:truncations[(p-1)*bands+id]),threshold=Math.Max(t,refT);bool hasReference=reference!=null;
                    significanceBits+=band.Significance;
                    for(int s=0;s<band.Significance;s++){
                        bool skip=true;int end=Math.Min(s*8+8,band.Groups);
                        for(int group=s*8;group<end;group++){
                            int value=Math.Max(old.G[row+group],t),prediction=hasReference?Math.Max(reference.G[refRow+group],threshold):t;
                            if(value!=prediction){skip=false;break;}
                        }
                        if(!skip)for(int group=s*8;group<end;group++){
                            int value=Math.Max(old.G[row+group],t),prediction=hasReference?Math.Max(reference.G[refRow+group],threshold):t;
                            int delta=value-prediction,room=prediction-t;
                            int unary=!hasReference?value-t:(delta<0?-2*delta-1:(delta<=room?2*delta:delta+room));
                            Require(unary>=0&&unary<=31,"Encoded unary value is out of range.");unaryBits+=unary+1;
                        }
                    }
                    int start=line*band.Stride;
                    for(int group=0;group<band.Groups;group++){
                        int g=old.G[row+group],planes=Math.Max(0,g-t),i=start+group*4;dataNibbles+=planes;
                        if(t==old.T){for(int k=0;k<4;k++)if((old.C[i+k]&32767)!=0)signBits++;}
                        else if(planes>0){var codes=Quantization(g,old.T,t).Codes;for(int k=0;k<4;k++)if(codes[(old.C[i+k]&32767)>>old.T]!=0)signBits++;}
                    }
                }
                int dataBytes=(dataNibbles+1)/2,gcliBytes=(unaryBits+7)/8,signBytes=(signBits+7)/8;
                Require(dataBytes<(1<<20)&&gcliBytes<(1<<20)&&signBytes<(1<<15),"Encoded packet size overflow.");
                bytes+=7+(significanceBits+7)/8+gcliBytes+dataBytes+signBytes;
            }
        }
        return checked((int)bytes);
    }
    public static EncodedRawFrame ForBudget(FrameState frame,int maximumBytes,ref int hint,CancellationToken cancellation=default(CancellationToken)){
        cancellation.ThrowIfCancellationRequested();
        int steps=Math.Max(0,Math.Min(200,hint)),size=EncodedSize(frame,steps,cancellation);
        while(size>maximumBytes&&steps<200){steps++;size=EncodedSize(frame,steps,cancellation);}
        if(size>maximumBytes)throw new InvalidDataException("The requested size could not be reached within the supported precision range.");
        while(steps>0){int candidate=EncodedSize(frame,steps-1,cancellation);if(candidate>maximumBytes)break;steps--;size=candidate;}
        var best=Encode(frame,steps,true,cancellation);Require(best.Bytes.Length==size,"Predicted and actual encoded sizes differ.");
        hint=steps;VerifyEncoded(frame,best,cancellation);best.Expected=null;return best;
    }
}
}
