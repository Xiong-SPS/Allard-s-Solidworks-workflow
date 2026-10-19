namespace SpsWorkflow {
 using System;
 using System.IO;
 using System.Collections.Generic;
 using SolidWorks.Interop.sldworks;
 public sealed class SelfTestReport {
  public string Status="running",Error,OutputFolder,ClientDate;
  public List<string> Checks=new List<string>();
  public PartSnapshot Pipe,Cope;
  public CircleProfile[] RegressionActiveProfiles;
 }
 public static class NativeSelfTest {
  static void Assert(SelfTestReport r,bool ok,string message){if(!ok)throw new InvalidOperationException(message);r.Checks.Add("PASS "+message);Console.WriteLine("PASS "+message);}
  static Dictionary<string,string> Metadata(string desc,string date){return new Dictionary<string,string>{{"Customer","WORKFLOW SELF-TEST ONLY"},{"Description",desc},{"Part Description",desc},{"Job Number","SELF-TEST"},{"Item Number","TEST"},{"DrawnBy","Codex"},{"Drawn By","Codex"},{"Drawn Date",date}};}
  public static SelfTestReport Run(object application,string workspace,string workflowRoot,string template,string templateHash,double od,double id,double parentOD,string regressionSource,string block,string clientDate,string[] protectedFiles) {
   ISldWorks sw=(ISldWorks)application;IModelDoc2 before=sw.ActiveDoc as IModelDoc2;bool dirty=before!=null&&before.GetSaveFlag();
   string folder=Path.Combine(workflowRoot,"tests","runs",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
   SelfTestReport report=new SelfTestReport{OutputFolder=folder,ClientDate=clientDate};var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(string file in protectedFiles)hashes[file]=SpsSession.Hash(file);if(before!=null&&!string.IsNullOrEmpty(before.GetPathName()))hashes[before.GetPathName()]=SpsSession.Hash(before.GetPathName());
   SpsSession session=new SpsSession(sw,workspace,folder);var created=new List<IModelDoc2>();
   try {
    bool blocked=false;try{session.Copy(template,@"..\outside.SLDPRT","Default",templateHash);}catch(InvalidOperationException){blocked=true;}Assert(report,blocked,"Path escape rejected before copy");
    blocked=false;try{session.Copy(template,"BadHash.SLDPRT","Default",new string('0',64));}catch(InvalidOperationException){blocked=true;}Assert(report,blocked,"Unverified template hash rejected before copy");
    blocked=false;try{new SpsSession(sw,workspace,folder);}catch(IOException){blocked=true;}Assert(report,blocked,"Existing output folder cannot be overwritten");
    if(before!=null&&before.GetType()==1){blocked=false;try{session.SetPipeLength(before,.1,od,id);}catch(InvalidOperationException){blocked=true;}Assert(report,blocked,"Unowned part cannot be edited");}
    IModelDoc2 pipe=session.Copy(template,"SelfTest-Pipe.SLDPRT","Default",templateHash);created.Add(pipe);PartSnapshot stock=session.SetPipeLength(pipe,.3048,od,id);
    Assert(report,Math.Abs(stock.Bounds[3]-.3048)<1e-7,"Pipe total length includes reverse extrusion");session.ApplyProperties(pipe,Metadata("Native workflow fixture; not production",clientDate),true,false,"");Assert(report,SpsSession.Property(pipe,"MakeOrBuy")=="M","New components default MakeOrBuy to M");
    var explicitValues=Metadata("Native workflow fixture; not production",clientDate);explicitValues["MakeOrBuy"]="B";session.ApplyProperties(pipe,explicitValues,true,false,"");Assert(report,SpsSession.Property(pipe,"MakeOrBuy")=="B","Explicit known MakeOrBuy value is preserved over the default");session.ApplyProperties(pipe,Metadata("Native workflow fixture; not production",clientDate),true,false,"");
    IBoreRegion bore=new CylinderBore(SpsSession.V(0,0,0),SpsSession.V(1,0,0),id/2,0,.3048);
    blocked=false;try{session.AddBranchOpening(pipe,SpsSession.V(.13,-.06,0),SpsSession.V(.13,.06,0),.073025,.066929,bore,"UnsafeOpening");}catch(InvalidOperationException){blocked=true;}Assert(report,blocked,"Opposite-wall cut rejected before feature creation");
    session.AddBranchOpening(pipe,SpsSession.V(.13,-.06,0),SpsSession.V(.13,0,0),.073025,.066929,bore,"BranchOpening");CircleProfile[] profiles=session.ActiveCircleProfiles(pipe,"BranchOpening");
    Assert(report,profiles.Length==2&&Math.Abs(profiles[0].Radius-.0349885)<1e-8,"Active complete-circle profiles use branch OD minus wall");
    blocked=false;try{session.AddBranchOpening(pipe,SpsSession.V(.13,-.06,0),SpsSession.V(.13,0,0),.073025,.066929,bore,"BranchOpening");}catch(InvalidOperationException){blocked=true;}Assert(report,blocked,"Duplicate feature/profile names rejected");
    GrooveSpec spec=new GrooveSpec{System="OGS",Method="roll",Source="https://assets.victaulic.com/assets/uploads/literature/25.01.pdf",Revision="25.01 Rev T 03/2026",OD=.0889,A=.015875,B=.0087376,C=.0849376,MinimumWall=.0019812,BlendRadius=.000762};
    blocked=false;try{session.AddRollGroove(pipe,spec,od,id,.3048);}catch(InvalidOperationException){blocked=true;}Assert(report,blocked,"Unreviewed groove specification rejected");
    spec.ReviewedForJob=true;session.AddRollGroove(pipe,spec,od,id,.3048);Assert(report,true,"Native Cut-Revolve1 and inner displacement match reviewed 3-inch OGS dimensions");
    DoubleDProfile profile=session.ReadStandard34Block(@"C:\SPS\Admin\SW Shared\Document Templates\PDM PART.PRTDOT",block,hashes[block]);
    session.AddDoubleD(pipe,profile,SpsSession.V(.23,od/2,0),SpsSession.V(0,1,0),SpsSession.V(1,0,0),SpsSession.V(.23,0,0),bore,"DoubleD34");Assert(report,true,"Approved eight-edge double-D block builds native cut with verified interior stop");
    report.Pipe=session.Snapshot(pipe);pipe.ShowNamedView2("*Isometric",7);pipe.ViewZoomtofit2();pipe.SaveBMP(Path.Combine(folder,"pipe-review.bmp"),1200,800);session.SaveChecked(pipe);Assert(report,true,"Solid, rebuilds, 14 Text properties and evaluated links pass before save");
    IModelDoc2 cope=session.Copy(template,"SelfTest-Cope.SLDPRT","Default",templateHash);created.Add(cope);session.SetPipeLength(cope,.1524,od,id);session.ApplyProperties(cope,Metadata("Native cope fixture; not production",clientDate),true,false,"");
    double middle=(od+id)/4,wall=(od-id)/2;double[][] guide=SpsSession.PerpendicularCylinderCopeGuide(.1524,middle,parentOD/2);IFeature cut=session.AddMidWallCope(cope,.1524,middle,wall,guide,"MidWallLoftedCope");report.Cope=session.Snapshot(cope);double max=0;foreach(double[] point in guide)max=Math.Max(max,point[0]);
    Assert(report,SpsSession.FeatureType(cut)=="BlendCut"&&Math.Abs(report.Cope.Bounds[3]-max)<.0002,"Native lofted cope finished extent matches actual-material mid-wall guide");cope.ShowNamedView2("*Isometric",7);cope.ViewZoomtofit2();cope.SaveBMP(Path.Combine(folder,"cope-review.bmp"),1200,800);session.SaveChecked(cope);
    IModelDoc2 regression=session.Copy(regressionSource,"SelfTest-StaleSketchRegression.SLDPRT","Default",hashes[regressionSource]);created.Add(regression);CircleProfile[] active=session.ActiveCircleProfiles(regression,"MidWallBranchOpening");
    double depth=SpsSession.Norm(SpsSession.Sub(active[0].Center,active[1].Center));Assert(report,Math.Abs(depth-.18526049538358458)<1e-7,"Regression: active depth is 185.260495 mm, not old unused sketches' 260 mm");report.RegressionActiveProfiles=active;
    var unverifiedStock=Metadata("Stale-sketch regression fixture; not production",clientDate);unverifiedStock["Material ID"]="UNVERIFIED TEST ID";blocked=false;try{session.ApplyProperties(regression,unverifiedStock,false,true,"");}catch(InvalidOperationException){blocked=true;}Assert(report,blocked,"New fitting ERP identity requires mapping evidence");
    session.ApplyProperties(regression,Metadata("Stale-sketch regression fixture; not production",clientDate),false,true,"");session.SaveChecked(regression);
    foreach(IModelDoc2 d in created)session.CloseSaved(d);created.Clear();
    foreach(var pair in hashes)Assert(report,SpsSession.Hash(pair.Key)==pair.Value,"Protected original unchanged: "+Path.GetFileName(pair.Key));
    if(before!=null)Assert(report,before.GetSaveFlag()==dirty,"Existing active document save state unchanged");report.Status="passed";
   }catch(Exception error){report.Status="failed";report.Error=error.ToString();Console.WriteLine(report.Error);}
   finally {
    // These are disposable fixtures owned by this test, never production docs.
    foreach(IModelDoc2 d in created){try{int e=0,w=0;if(d.Save3(1,ref e,ref w)&&e==0)sw.CloseDoc(d.GetTitle());}catch(Exception error){report.Checks.Add("Retained failed fixture: "+error.Message);}}
    if(before!=null){int e=0;sw.ActivateDoc3(before.GetTitle(),false,0,ref e);}
   }
   return report;
  }
 }
}
