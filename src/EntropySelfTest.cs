using System;
using System.IO;
namespace Rawer {
internal static class EntropySelfTest {
    public static void Run(){
        var reused=new BitOutput();for(int i=0;i<16;i++)reused.Bit(1);reused.Seal();reused.Reset();reused.Nibble(0);if(!Bytes.Equal(reused.Finish(),new byte[]{0}))throw new Exception("Packet writer leaked bits across reset.");
        Console.WriteLine("PASS reused packet buffers do not leak prior bits");
        for(int i=0;i<100;i++){reused.Reset();reused.Nibble(i&15);reused.Nibble((i>>4)&15);byte[] b=reused.Finish();if(b.Length!=1||b[0]!=(((i&15)<<4)|((i>>4)&15)))throw new Exception("Repeated packet reset changed data.");}
        Console.WriteLine("PASS reused packet buffers preserve successive byte patterns");
    }
}
}
