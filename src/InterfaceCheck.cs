using System;
using System.IO;
using System.Windows.Forms;

namespace Rawer {
public sealed partial class MainForm {
    internal void RunInterfaceChecks(){
        int passed=0;Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("UI test failed: "+name);passed++;Console.WriteLine("PASS "+name);};
        ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new System.Drawing.Point(-20000,-20000);Show();CreateChildren(this);Application.DoEvents();
        try{
            var job=new ClipJob{SourcePath="C:\\ExampleMedia\\sample.R3D",SourceBytes=1000000,Frames=10};jobs.Add(job);AddRow(job);RefreshQueue();
            check(targetRatio==2&&mode2.Selected&&!mode4.Selected,"2x is the initial UI mode");
            mode4.PerformClick();check(targetRatio==4&&mode4.Selected&&!mode2.Selected&&job.TargetRatio==4&&namingLabel.Text.Contains("4x"),"clicking 4x changes mode, source state and output suffix");
            mode2.PerformClick();check(targetRatio==2&&mode2.Selected&&!mode4.Selected&&job.TargetRatio==2,"clicking 2x restores the other radio choice");
            check(tips.GetToolTip(mode2).Contains("50%")&&tips.GetToolTip(mode4).Contains("25%")&&tips.GetToolTip(mode4).Contains("DaVinci"),"both mode buttons have detailed hover explanations");
            busy=true;RefreshButtons();check(!mode2.Enabled&&!mode4.Enabled&&!start.Enabled,"compression selection is locked while processing");busy=false;RefreshButtons();
            for(int i=0;i<30;i++){var item=new ClipJob{SourcePath="C:\\ExampleMedia\\clip"+i+".R3D",SourceBytes=100};jobs.Add(item);AddRow(item);}RefreshQueue();Application.DoEvents();int formHeight=Height;grid.FirstDisplayedScrollingRowIndex=20;
            check(grid.FirstDisplayedScrollingRowIndex==20&&Height==formHeight&&grid.DisplayedRowCount(false)<grid.RowCount,"a large source queue scrolls inside its comic panel");
            job.Outputs.Add(new CompletedClip{SourceId=job.Id,SourcePath=job.SourcePath,OutputPath="C:\\ExampleMedia\\sample_RAWER_4x.R3D",TargetRatio=4,SourceBytes=1000000,OutputBytes=250000});job.Status=JobStatus.Completed;RefreshResults();
            ClearQueue();check(jobs.Count==0&&resultGrid.Rows.Count==1&&outputs.Count==1,"clearing sources retains finished files in panel 04");
            using(var bitmap=new System.Drawing.Bitmap(ClientSize.Width,ClientSize.Height))Controls[0].DrawToBitmap(bitmap,new System.Drawing.Rectangle(System.Drawing.Point.Empty,ClientSize));
            int cacheBuilds=ComicFrame.CacheBuilds;WindowState=FormWindowState.Minimized;Application.DoEvents();WindowState=FormWindowState.Normal;Application.DoEvents();
            using(var bitmap=new System.Drawing.Bitmap(ClientSize.Width,ClientSize.Height))Controls[0].DrawToBitmap(bitmap,new System.Drawing.Rectangle(System.Drawing.Point.Empty,ClientSize));
            check(ComicFrame.CacheBuilds==cacheBuilds,"restore reuses all illustrated panel caches");
            check(GetType().Assembly.GetManifestResourceNames().Length>=4,"comic illustrations are embedded in the standalone application");
            Console.WriteLine("UI SELFTEST "+passed+" passed");
        }finally{busy=false;Hide();}
    }
}
}
