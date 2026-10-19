namespace SpsWorkflow {
 using System;
 using System.IO;
 using System.Collections.Generic;
 using SolidWorks.Interop.sldworks;
 public sealed class CopedDescriptionReport {
  public string Status="running",Error,OutputFolder,PartDescription,MaxModeledLength,StockLength;
  public double FinishedLengthInches;
  public List<string> Checks=new List<string>();
 }
 public static class CopedDescriptionSelfTest {
  static void Assert(CopedDescriptionReport r,bool ok,string text){if(!ok)throw new InvalidOperationException(text);r.Checks.Add("PASS "+text);Console.WriteLine("PASS "+text);}
  public static CopedDescriptionReport Run(object application,string workspace,string workflowRoot,string template,string hash) {
   ISldWorks app=(ISldWorks)application;IModelDoc2 before=app.ActiveDoc as IModelDoc2;bool dirty=before!=null&&before.GetSaveFlag();string folder=Path.Combine(workflowRoot,"tests","coped-description-runs",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));var r=new CopedDescriptionReport{OutputFolder=folder};var session=new SpsSession(app,workspace,folder);IModelDoc2 repair=null;
   try {
    double stock=.1524,od=.0889,id=.082804;var source=new RecipeSource{Template=template,TemplateSha256=hash,Configuration="Default",Filename="CopedDescriptionTest-01.SLDPRT",Properties=new Dictionary<string,string>{{"Customer","SYNTHETIC COPE DESCRIPTION TEST"}}};
    var recipe=new PipeRecipe{Source=source,StockLength=stock,OD=od,ID=id,Cope=new CopeRecipe{GuidePoints=SpsSession.PerpendicularCylinderCopeGuide(stock,(od+id)/4,.0841375)}};
    RecipeResult result=PartRecipes.Pipe(session,recipe);r.FinishedLengthInches=(result.Snapshot.Bounds[3]-result.Snapshot.Bounds[0])/.0254;string formatted=PropertyLengthFormat.Format(r.FinishedLengthInches);r.PartDescription=result.Verification.Properties["Part Description"].Evaluated;r.MaxModeledLength=result.Verification.Properties["Max Modeled Length"].Evaluated;r.StockLength=result.Verification.Properties["Stock Length"].Evaluated;
    Assert(r,result.Verification.Expected.CopedPipe,"Coped recipe automatically selects finished-length descriptions");
    Assert(r,r.FinishedLengthInches<6&&r.PartDescription.Contains(formatted)&&!r.PartDescription.Contains("6.000"),"Part Description contains actual maximum finished axial extent, not six-inch stock length");
    Assert(r,r.MaxModeledLength==formatted+" in"&&!PropertyLengthFormat.ExcessDecimals(r.MaxModeledLength),"Max Modeled Length is measured from final solid and rounded to at most three decimals");
    Assert(r,r.StockLength=="6.000 in"&&result.Verification.Properties["MaterialQty"].Evaluated=="6.000","Stock Length and MaterialQty preserve the separate stock length");
    Assert(r,result.Closed&&app.GetOpenDocumentByName(result.File)==null,"Completed coped component is saved and closed");
    repair=session.Copy(result.File,"CopedDescriptionTest-02.SLDPRT","Default",SpsSession.Hash(result.File));repair.Extension.CustomPropertyManager[""].Add3("Part Description",30,"WRONG STOCK LENGTH",2);repair.Extension.CustomPropertyManager[""].Add3("Max Modeled Length",30,"999 in",2);PartVerification corrected=session.CompletePart(repair,new PartExpectations{CopedPipe=true});repair=null;
    Assert(r,corrected.Properties["Part Description"].Evaluated.Contains(formatted)&&corrected.Properties["Max Modeled Length"].Evaluated==formatted+" in","Final verification refreshes stale length text from the actual solid");
    Assert(r,corrected.Properties["MaterialQty"].Evaluated=="6.000"&&Math.Abs(corrected.Snapshot.Bounds[3]-result.Snapshot.Bounds[3])<1e-10,"Description refresh preserves stock quantities and native geometry");
    Assert(r,SpsSession.Hash(template)==hash,"Original pipe library blank unchanged");r.Status="passed";
   }catch(Exception error){r.Status="failed";r.Error=error.ToString();Console.WriteLine(r.Error);}
   finally{if(repair!=null){try{session.SaveChecked(repair);session.CloseSaved(repair);}catch(Exception){}}if(before!=null){if(before.GetSaveFlag()!=dirty){r.Status="failed";r.Error="Original active document save state changed";}int e=0;app.ActivateDoc3(before.GetTitle(),false,0,ref e);}}
   return r;
  }
 }
}
