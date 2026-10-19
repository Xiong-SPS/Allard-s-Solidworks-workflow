namespace SpsWorkflow {
 using System;
 using System.IO;
 using System.Collections.Generic;
 using SolidWorks.Interop.sldworks;
 public sealed class RecipeTestReport {
  public string Status="running",Error,OutputFolder;
  public List<string> Checks=new List<string>();
  public RecipeResult Pipe,Elbow,Flange;
 }
 public static class RecipeSelfTest {
  static void Assert(RecipeTestReport r,bool ok,string message){if(!ok)throw new InvalidOperationException(message);r.Checks.Add("PASS "+message);Console.WriteLine("PASS "+message);}
  static RecipeSource Source(string path,string hash,string filename){return new RecipeSource{Template=path,TemplateSha256=hash,Configuration="Default",Filename=filename,Properties=new Dictionary<string,string>{{"Description","NONPRODUCTION RECIPE TEST"},{"Part Description","NONPRODUCTION RECIPE TEST"},{"Job Number","RecipeDemo"},{"Customer","SYNTHETIC WORKFLOW TEST"}}};}
  static IModelDoc2 Open(ISldWorks app,string path){int e=0,w=0;IModelDoc2 d=app.OpenDoc6(path,1,1,"",ref e,ref w) as IModelDoc2;if(d==null||e!=0)throw new InvalidOperationException("Read-back open failed: "+e);return d;}
  public static RecipeTestReport Run(object application,string workspace,string workflowRoot,string pipeTemplate,string pipeHash,string elbowTemplate,string elbowHash,string flangeTemplate,string flangeHash,string block,string blockHash,string evidence,string[] protectedFiles) {
   ISldWorks app=(ISldWorks)application;IModelDoc2 before=app.ActiveDoc as IModelDoc2;bool dirty=before!=null&&before.GetSaveFlag();
   string folder=Path.Combine(workflowRoot,"tests","recipe-runs",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
   var r=new RecipeTestReport{OutputFolder=folder};var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(string f in protectedFiles)hashes[f]=SpsSession.Hash(f);if(before!=null&&!string.IsNullOrEmpty(before.GetPathName()))hashes[before.GetPathName()]=SpsSession.Hash(before.GetPathName());
   var session=new SpsSession(app,workspace,folder);IModelDoc2 inspecting=null,guard=null;
   try {
    string plannedFolder=Path.Combine(folder,"PlanningHandoff");Directory.CreateDirectory(plannedFolder);string plan=Path.Combine(plannedFolder,"component-plan-v1.json");File.WriteAllText(plan,"{\"notice\":\"synthetic session handoff test only\"}");string planHash=SpsSession.Hash(plan);
    SpsSession plannedSession=SpsSession.ForPlannedJob(app,workspace,plannedFolder,plan,planHash);Assert(r,plannedSession!=null&&SpsSession.Hash(plan)==planHash,"Reviewed planning folder accepted without changing its plan");
    bool planBlocked=false;try{SpsSession.ForPlannedJob(app,workspace,plannedFolder,plan,new string('0',64));}catch(InvalidOperationException){planBlocked=true;}Assert(r,planBlocked,"Changed plan rejected before starting modeling session");
    string marker=Path.Combine(plannedFolder,"Existing.SLDPRT");File.WriteAllText(marker,"Synthetic overwrite guard, not a model");planBlocked=false;try{SpsSession.ForPlannedJob(app,workspace,plannedFolder,plan,planHash);}catch(InvalidOperationException){planBlocked=true;}Assert(r,planBlocked,"Planning handoff refuses an existing native model folder");
    var recipe=new PipeRecipe{Source=Source(pipeTemplate,pipeHash,"RecipeDemo-01.SLDPRT"),StockLength=.4064,OD=.0889,ID=.082804};
    recipe.Cope=new CopeRecipe{GuidePoints=SpsSession.PerpendicularCylinderCopeGuide(recipe.StockLength,(recipe.OD+recipe.ID)/4,.0841375)};
    recipe.Groove=new GrooveSpec{System="OGS",Method="roll",Source="https://assets.victaulic.com/assets/uploads/literature/25.01.pdf",Revision="25.01 Rev T 03/2026",OD=.0889,A=.015875,B=.0087376,C=.0849376,MinimumWall=.0019812,BlendRadius=.000762};
    bool blocked=false;try{PartRecipes.Pipe(session,recipe);}catch(ArgumentException){blocked=true;}Assert(r,blocked&&Directory.GetFiles(folder).Length==0,"Unreviewed groove recipe rejected before copying");
    recipe.Groove.ReviewedForJob=true;IBoreRegion bore=new CylinderBore(SpsSession.V(0,0,0),SpsSession.V(1,0,0),recipe.ID/2,0,recipe.StockLength);
    recipe.Openings.Add(new OpeningRecipe{Name="BranchOpening",OutsideStart=SpsSession.V(.10,-.06,0),InteriorStop=SpsSession.V(.10,0,0),BranchOD=.073025,BranchID=.066929,ParentBore=bore});
    DoubleDProfile profile=session.ReadStandard34Block(@"C:\SPS\Admin\SW Shared\Document Templates\PDM PART.PRTDOT",block,blockHash);
    recipe.DoubleDOpenings.Add(new DoubleDRecipe{Name="DoubleD34",Block=profile,WeldCenter=SpsSession.V(.20,recipe.OD/2,0),Outward=SpsSession.V(0,1,0),LongAxis=SpsSession.V(1,0,0),InteriorStop=SpsSession.V(.20,0,0),ParentBore=bore});
    r.Pipe=PartRecipes.Pipe(session,recipe);Assert(r,r.Pipe.Saved&&r.Pipe.Closed&&app.GetOpenDocumentByName(r.Pipe.File)==null,"Combined pipe recipe saved and closed its component");
    Assert(r,r.Pipe.Snapshot.SolidCount==1&&r.Pipe.Snapshot.BodyCheck==0,"Combined cope/groove/openings recipe produces one valid solid");
    inspecting=Open(app,r.Pipe.File);IPartDoc part=(IPartDoc)inspecting;
    Assert(r,SpsSession.FeatureType(part.FeatureByName("MidWallLoftedCope") as IFeature)=="BlendCut","Combined recipe retains native lofted cope");
    Assert(r,part.FeatureByName("Cut-Revolve1")!=null&&part.FeatureByName("RolledWallInnerDisplacement")!=null,"Combined recipe retains named groove and inner-wall displacement");
    Assert(r,session.ActiveCircleProfiles(inspecting,"BranchOpening").Length==2&&part.FeatureByName("DoubleD34")!=null,"Both round and approved double-D openings exist after reload");
    Assert(r,SpsSession.Property(inspecting,"MakeOrBuy")=="M"&&SpsSession.Property(inspecting,"Material ID")==r.Pipe.Snapshot.MaterialId,"Pipe recipe preserves stock identifier and defaults MakeOrBuy");
    inspecting.ShowNamedView2("*Isometric",7);inspecting.ViewZoomtofit2();inspecting.SaveBMP(Path.Combine(folder,"combined-pipe-review.bmp"),1200,800);app.CloseDoc(inspecting.GetTitle());inspecting=null;
    var elbow=new ElbowRecipe{Source=Source(elbowTemplate,elbowHash,"RecipeDemo-02.SLDPRT"),OriginalAngle=Math.PI/2,TargetAngle=40*Math.PI/180,BendRadius=.2286,OD=.168275,ID=.1614678};
    r.Elbow=PartRecipes.Elbow(session,elbow);Assert(r,r.Elbow.Saved&&r.Elbow.Closed&&app.GetOpenDocumentByName(r.Elbow.File)==null,"Trimmed library elbow recipe saved and closed");
    inspecting=Open(app,r.Elbow.File);Assert(r,Math.Abs(((IDimension)inspecting.Parameter("D1@Revolve1")).SystemValue-elbow.TargetAngle)<1e-8,"Reloaded elbow retains native 40-degree angle");
    Assert(r,Math.Abs(((IDimension)inspecting.Parameter("D1@Sketch1")).SystemValue-elbow.OD)<1e-8&&Math.Abs(((IDimension)inspecting.Parameter("D3@Sketch1")).SystemValue-(elbow.OD-elbow.ID)/2)<1e-8,"Elbow native stock OD and wall remain unchanged");
    Assert(r,SpsSession.Property(inspecting,"Material ID")==""&&!string.IsNullOrWhiteSpace(SpsSession.Property(inspecting,"Template Stock Identifier")),"Modified elbow keeps template provenance and flags missing ERP mapping");
    inspecting.ShowNamedView2("*Isometric",7);inspecting.ViewZoomtofit2();inspecting.SaveBMP(Path.Combine(folder,"elbow-review.bmp"),1200,800);app.CloseDoc(inspecting.GetTitle());inspecting=null;
    var flange=new FittingRecipe{Source=Source(flangeTemplate,flangeHash,"RecipeDemo-03.SLDPRT"),DimensionEvidence=evidence,Edits=new DimensionEdit[]{new DimensionEdit{Name="D1@Sketch2",Expected=.0762,Target=.079375}},PreservedDimensions=new Dictionary<string,double>{{"D1@Boss-Extrude1",.0127},{"D1@Sketch1",.14605},{"D2@Sketch1",.14605}}};
    r.Flange=PartRecipes.Fitting(session,flange);Assert(r,r.Flange.Saved&&r.Flange.Closed,"Library flange dimension recipe saved and closed");
    inspecting=Open(app,r.Flange.File);Assert(r,Math.Abs(((IDimension)inspecting.Parameter("D1@Sketch2")).SystemValue-.079375)<1e-8&&Math.Abs(((IDimension)inspecting.Parameter("D1@Boss-Extrude1")).SystemValue-.0127)<1e-8,"Flange bore changed through existing sketch with thickness preserved");
    bool boreFound=false;foreach(IFace2 face in (object[])((IBody2)((object[])((IPartDoc)inspecting).GetBodies2(0,false))[0]).GetFaces()){ISurface surface=face.GetSurface() as ISurface;if(surface.IsCylinder()&&Math.Abs(((double[])surface.CylinderParams)[6]-.079375/2)<1e-8)boreFound=true;}Assert(r,boreFound,"Flange actual cylindrical bore matches recipe, beyond dimension text");
    app.CloseDoc(inspecting.GetTitle());inspecting=null;
    guard=session.Copy(flangeTemplate,"RecipeDemo-Guard.SLDPRT","Default",flangeHash);
    blocked=false;try{session.EditExistingDimensions(guard,new DimensionEdit[]{new DimensionEdit{Name="D1@Sketch2",Expected=.0762,Target=.08},new DimensionEdit{Name="NoDimension@Sketch2",Expected=.01,Target=.02}},new Dictionary<string,double>{{"D1@Boss-Extrude1",.0127}});}catch(InvalidOperationException){blocked=true;}
    Assert(r,blocked&&Math.Abs(((IDimension)guard.Parameter("D1@Sketch2")).SystemValue-.0762)<1e-8,"All dimension references validated before any edit");
    blocked=false;try{session.EditExistingDimensions(guard,new DimensionEdit[]{new DimensionEdit{Name="D1@Sketch2",Expected=.077,Target=.08}},new Dictionary<string,double>{{"D1@Boss-Extrude1",.0127}});}catch(InvalidOperationException){blocked=true;}Assert(r,blocked,"Stale expected native dimension rejected");
    session.ApplyProperties(guard,Source(flangeTemplate,flangeHash,"unused").Properties,false,true,"");session.SaveChecked(guard);session.CloseSaved(guard);guard=null;
    int files=Directory.GetFiles(folder,"*.SLDPRT").Length;elbow.Source.Filename="RecipeDemo-BadStock.SLDPRT";elbow.Source.Properties["Material ID"]="UNVERIFIED";
    blocked=false;try{PartRecipes.Elbow(session,elbow);}catch(ArgumentException){blocked=true;}Assert(r,blocked&&Directory.GetFiles(folder,"*.SLDPRT").Length==files,"Unverified modified stock identity rejected before copying");
    Assert(r,app.GetOpenDocumentByName(r.Pipe.File)==null&&app.GetOpenDocumentByName(r.Elbow.File)==null&&app.GetOpenDocumentByName(r.Flange.File)==null,"All completed recipe components are closed");r.Status="passed";
   }catch(Exception error){r.Status="failed";r.Error=error.ToString();Console.WriteLine(r.Error);}
   finally {
    if(inspecting!=null)app.CloseDoc(inspecting.GetTitle());
    if(guard!=null){try{session.SaveChecked(guard);session.CloseSaved(guard);}catch(Exception error){r.Checks.Add("Retained failed guard: "+error.Message);}}
    foreach(var pair in hashes){bool same=SpsSession.Hash(pair.Key)==pair.Value;r.Checks.Add((same?"PASS ":"FAIL ")+"Protected original unchanged: "+Path.GetFileName(pair.Key));if(!same){r.Status="failed";r.Error="Protected file hash changed: "+pair.Key;}}
    if(before!=null){bool same=before.GetSaveFlag()==dirty;r.Checks.Add((same?"PASS ":"FAIL ")+"Existing active document save state unchanged");if(!same){r.Status="failed";r.Error="Existing document save state changed";}int e=0;app.ActivateDoc3(before.GetTitle(),false,0,ref e);}
   }
   return r;
  }
 }
}
