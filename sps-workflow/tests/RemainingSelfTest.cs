namespace SpsWorkflow {
 using System;
 using System.IO;
 using System.Collections.Generic;
 using SolidWorks.Interop.sldworks;
 public sealed class RemainingReport {
  public string Status="running",Error,OutputFolder;
  public List<string> Checks=new List<string>();
  public BatchReport Batch;
  public AssemblyVerification Assembly;
 }
 public static class RemainingSelfTest {
  static void Assert(RemainingReport r,bool ok,string message){if(!ok)throw new InvalidOperationException(message);r.Checks.Add("PASS "+message);Console.WriteLine("PASS "+message);}
  static RecipeSource Source(string template,string hash,string name,string plan,string planHash,string id){return new RecipeSource{Template=template,TemplateSha256=hash,Configuration="Default",Filename=name,PlanFile=plan,PlanSha256=planHash,PartId=id,Properties=new Dictionary<string,string>{{"Customer","SYNTHETIC WORKFLOW TEST"},{"Job Number","RemainingDemo"}}};}
  public static RemainingReport Run(object application,string workspace,string workflowRoot,string template,string hash,string block,string blockHash) {
   ISldWorks app=(ISldWorks)application;IModelDoc2 before=app.ActiveDoc as IModelDoc2;bool dirty=before!=null&&before.GetSaveFlag();string originalHash=SpsSession.Hash(template),originalBlockHash=SpsSession.Hash(block);
   string folder=Path.Combine(workflowRoot,"tests","remaining-runs",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));var r=new RemainingReport{OutputFolder=folder};var session=new SpsSession(app,workspace,folder);IModelDoc2 guard=null,assembly=null;
   try {
    string plan=Path.Combine(folder,"component-plan-v1.json");File.WriteAllText(plan,"{\"notice\":\"Synthetic native batch test, not a production plan\"}");string planHash=SpsSession.Hash(plan);bool dependentCalled=false;
    var first=new PipeRecipe{Source=Source(template,hash,"RemainingDemo-01.SLDPRT",plan,planHash,"good"),StockLength=.254,OD=.0889,ID=.082804};
    var second=new PipeRecipe{Source=Source(template,hash,"RemainingDemo-02.SLDPRT",plan,planHash,"independent"),StockLength=.3048,OD=.0889,ID=.082804};
    r.Batch=PartBatch.Run(app,workspace,folder,plan,planHash,new PartTask[]{
     new PartTask{Id="good",Build=delegate(){return PartRecipes.Pipe(session,first);}},
     new PartTask{Id="failure",Build=delegate(){throw new InvalidOperationException("Intentional nonproduction batch failure");}},
     new PartTask{Id="dependent",Dependencies=new string[]{"failure"},Build=delegate(){dependentCalled=true;throw new InvalidOperationException("Must not execute blocked task");}},
     new PartTask{Id="independent",Build=delegate(){return PartRecipes.Pipe(session,second);}}
    });
    Assert(r,r.Batch.Status=="incomplete"&&r.Batch.Parts[0].Status=="passed"&&r.Batch.Parts[1].Status=="failed"&&r.Batch.Parts[2].Status=="blocked"&&r.Batch.Parts[3].Status=="passed"&&!dependentCalled,"Failed task blocks its dependency while independent modeling completes");
    Assert(r,Directory.GetFiles(folder,"batch-*.json").Length>=5,"Batch progress checkpoints persist before the next part");
    foreach(int index in new int[]{0,3}){RecipeResult result=r.Batch.Parts[index].Result;Assert(r,File.Exists(result.File+".verification.json")&&result.Verification.FileSha256==SpsSession.Hash(result.File)&&result.Verification.Expected.PlanSha256==planHash,"Per-part evidence binds current file, reviewed plan and closure: "+Path.GetFileName(result.File));Assert(r,result.Verification.Properties["Description"].Raw.Contains(Path.GetFileName(result.File))&&!result.Verification.Properties["Description"].Evaluated.Contains("@"),"Inherited pipe description links resolve to current part: "+Path.GetFileName(result.File));}
    DoubleDProfile a=session.ReadStandard34Block(@"C:\SPS\Admin\SW Shared\Document Templates\PDM PART.PRTDOT",block,blockHash);double old=a.Edges[0][1];a.Edges[0][1]=42;DoubleDProfile b=session.ReadStandard34Block("Cached block needs no new scratch document",block,blockHash);Assert(r,Math.Abs(b.Edges[0][1]-old)<1e-10,"Hash-checked block cache avoids another scratch load and isolates returned geometry");
    guard=session.Copy(template,"RemainingDemo-Guard.SLDPRT","Default",hash);session.SetPipeLength(guard,.1524,.0889,.082804);var known=new Dictionary<string,string>{{"Customer","SYNTHETIC WORKFLOW TEST"}};session.ApplyProperties(guard,known,true,false,"");
    var bad=new PartExpectations();bad.Features.Add(new FeatureExpectation{Name="UnusedOldSketch",Type="BlendCut"});bool blocked=false;try{session.VerifyAndSave(guard,bad);}catch(InvalidOperationException){blocked=true;}Assert(r,blocked&&!File.Exists(guard.GetPathName()+".verification.json"),"Missing active feature cannot produce a passed receipt");
    guard.Extension.CustomPropertyManager["Default"].Add3("Customer",30,"STALE OVERRIDE",2);blocked=false;try{session.VerifyAndSave(guard,new PartExpectations());}catch(InvalidOperationException){blocked=true;}Assert(r,blocked,"Stale configuration property override is rejected");session.ApplyProperties(guard,known,true,false,"");PartVerification fixedReceipt=session.CompletePart(guard,new PartExpectations());guard=null;Assert(r,fixedReceipt.Properties["Customer"].ConfigurationRaw==known["Customer"],"Known properties repair existing configuration overrides");
    assembly=app.NewDocument(@"C:\SPS\Admin\SW Shared\Document Templates\PDM ASSY.asmdot",0,0,0) as IModelDoc2;if(assembly==null)throw new InvalidOperationException("Default assembly template unavailable");IAssemblyDoc ad=(IAssemblyDoc)assembly;IMathUtility math=app.GetMathUtility() as IMathUtility;var expected=new List<PlannedOccurrence>();
    for(int i=0;i<2;i++){RecipeResult part=r.Batch.Parts[i==0?0:3].Result;int e=0,w=0;IModelDoc2 pd=app.OpenDoc6(part.File,1,1,"",ref e,ref w) as IModelDoc2;if(pd==null||e!=0)throw new InvalidOperationException("Open test component");app.ActivateDoc3(assembly.GetTitle(),false,0,ref e);IComponent2 c=ad.AddComponent5(part.File,0,"",false,"",0,0,0) as IComponent2;if(c==null)throw new InvalidOperationException("Insert test component");double[] t=new double[]{1,0,0,0,1,0,0,0,1,0,i*.2,0,1,0,0,0};c.ReferencedConfiguration="Default";c.Transform2=(MathTransform)math.CreateTransform(t);assembly.ClearSelection2(true);c.Select4(false,null,false);ad.FixComponent();assembly.ClearSelection2(true);app.CloseDoc(pd.GetTitle());expected.Add(new PlannedOccurrence{File=part.File,FileSha256=SpsSession.Hash(part.File),Configuration="Default",Transform=t});}
    assembly.ForceRebuild3(false);int error=0,warning=0;string asmFile=Path.Combine(folder,"RemainingDemo.SLDASM");if(File.Exists(asmFile)||!assembly.Extension.SaveAs(asmFile,0,1,null,ref error,ref warning)||error!=0)throw new InvalidOperationException("Save test assembly");assembly.Save3(1,ref error,ref warning);
    r.Assembly=AssemblyVerifier.Verify(app,assembly,expected.ToArray(),new string[0],Path.Combine(folder,"assembly-verification.json"),planHash);Assert(r,r.Assembly.Status=="passed"&&r.Assembly.Components.Count==2&&r.Assembly.MainAssemblyOpen,"Native assembly audit verifies saved references, ordered occurrences and all transforms");
    expected[1].Transform[10]+=.05;blocked=false;try{AssemblyVerifier.Verify(app,assembly,expected.ToArray(),new string[0],Path.Combine(folder,"wrong-placement.json"),planHash);}catch(InvalidOperationException){blocked=true;}Assert(r,blocked&&!File.Exists(Path.Combine(folder,"wrong-placement.json")),"Wrong assembly placement cannot produce passed final evidence");r.Status="passed";
   }catch(Exception error){r.Status="failed";r.Error=error.ToString();Console.WriteLine(r.Error);}
   finally{if(assembly!=null)app.CloseDoc(assembly.GetTitle());if(guard!=null){try{session.SaveChecked(guard);session.CloseSaved(guard);}catch(Exception error){r.Checks.Add("Retained failed guard: "+error.Message);}}if(SpsSession.Hash(template)!=originalHash||SpsSession.Hash(block)!=originalBlockHash){r.Status="failed";r.Error="Protected original changed";}else r.Checks.Add("PASS Protected pipe blank and block unchanged");if(before!=null){if(before.GetSaveFlag()!=dirty){r.Status="failed";r.Error="Original active document save state changed";}int e=0;app.ActivateDoc3(before.GetTitle(),false,0,ref e);}}
   return r;
  }
 }
}
