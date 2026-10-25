using System;
using System.IO;
using System.Web.Script.Serialization;
namespace Rawer {
internal static class PrivacySelfTest {
    public static void InspectSummary(string source){
        using(var movie=new Movie(source)){
            string json=Optimizer.Json(movie.Summary());var fields=new JavaScriptSerializer().DeserializeObject(json) as System.Collections.Generic.Dictionary<string,object>;
            if(fields==null||fields.ContainsKey("path")||Convert.ToString(fields["file"])!=Path.GetFileName(source))throw new Exception("Movie summary exposes its full path.");
        }
        Console.WriteLine("PASS actual media summary does not include its directory");
    }
}
}
