using System;
using System.IO;
using System.Text;

namespace Rawer {
public static class SelfTest {
    static int passed;
    static void Check(bool condition,string name){if(!condition)throw new Exception("Self-test failed: "+name);passed++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name){try{action();}catch(NotSupportedException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    static byte[] Fixture(int pad){
        using(var s=new MemoryStream()){
            byte[] prefix=new byte[1024];Bytes.P32(prefix,8,12);Encoding.ASCII.GetBytes("NRFM0100").CopyTo(prefix,12);Bytes.P32(prefix,20,892);Encoding.ASCII.GetBytes("NRFH").CopyTo(prefix,24);Bytes.P32(prefix,912,112);Encoding.ASCII.GetBytes("NRTH").CopyTo(prefix,916);Bytes.P32(prefix,920,16);Encoding.ASCII.GetBytes("NRTI0100").CopyTo(prefix,924);Bytes.P32(prefix,936,88);Encoding.ASCII.GetBytes("NRTA").CopyTo(prefix,940);Encoding.ASCII.GetBytes("NRAW").CopyTo(prefix,4);s.Write(prefix,0,prefix.Length);s.WriteByte(255);s.WriteByte(16);
            byte[] cap=new byte[36];cap[0]=255;cap[1]=80;cap[3]=34;s.Write(cap,0,cap.Length);
            byte[] pih=new byte[41];pih[0]=255;pih[1]=18;pih[3]=39;pih[12]=0x17;pih[13]=0xac;pih[14]=0x0d;pih[15]=0x52;pih[19]=16;pih[20]=4;pih[21]=4;pih[22]=8;pih[23]=18;pih[24]=0x44;pih[25]=0x10;pih[26]=0x51;pih[27]=0x14;int pihAt=(int)s.Position;s.Write(pih,0,pih.Length);
            byte[] cdt={255,19,0,14,14,17,81,14,17,81,14,17,0,14,17,81};s.Write(cdt,0,cdt.Length);byte[] wgt=new byte[54];wgt[0]=255;wgt[1]=20;wgt[3]=52;s.Write(wgt,0,wgt.Length);
            for(int i=0;i<853;i++){
                if(i%16==0){byte[] slice={255,32,0,4,(byte)((i/16)>>8),(byte)(i/16)};s.Write(slice,0,6);}
                byte[] h=new byte[12];Bytes.P24(h,0,8*22+pad);h[3]=3;h[4]=7;for(int j=5;j<11;j++)h[j]=255;h[11]=192;s.Write(h,0,h.Length);
                for(int k=0;k<8;k++){byte[] packet=new byte[22];packet[2]=8;packet[4]=0;packet[5]=128;packet[6]=1;packet[19]=(byte)(31+k);packet[20]=73;packet[21]=93;s.Write(packet,0,packet.Length);}
                s.Write(new byte[pad],0,pad);
            }
            s.WriteByte(255);s.WriteByte(17);var b=s.ToArray();Bytes.P32(b,0,b.Length);Bytes.P32(b,932,b.Length-1024);Bytes.P32(b,pihAt+4,b.Length-1024);return b;
        }
    }
    public static void Run(){
        byte[] source=Fixture(11),before=(byte[])source.Clone();var compact=NikonCodec.Compact(source);
        Check(compact.Removed==853*11,"removes only declared precinct padding");Check(Bytes.Equal(source,before),"source buffer remains unchanged");Check(compact.Precincts==853&&compact.Packets==6824,"all precincts and packets are visited");Check(Bytes.Equal(NikonCodec.Compact(compact.Frame).Frame,compact.Frame),"second pass is byte-identical");
        var changed=(byte[])source.Clone();changed[1024+149+6+12+176]=123;Reject(()=>NikonCodec.Compact(changed),"nonzero proposed padding is rejected");
        changed=(byte[])source.Clone();changed[932]++;Reject(()=>NikonCodec.Compact(changed),"stale image-size metadata is rejected");
        changed=(byte[])source.Clone();changed[1024+149+6+12]=127;Reject(()=>NikonCodec.Compact(changed),"packet exceeding its precinct is rejected");
        changed=(byte[])source.Clone();changed[changed.Length-1]=0;Reject(()=>NikonCodec.Compact(changed),"corrupt end marker is rejected");
        Console.WriteLine("SELFTEST "+passed+" passed");
    }
}
}
