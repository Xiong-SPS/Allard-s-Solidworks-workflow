namespace SpsWorkflow {
 using System;
 using System.IO;
 using System.Collections.Generic;
 using SolidWorks.Interop.sldworks;
 public sealed class LengthTestReport {
  public string Status="running",Error,OutputFolder,Quantity,Description;
  public List<string> Checks=new List<string>();
 }
 public static class LengthPropertySelfTest {
  static void Assert(LengthTestReport r,bool ok,string text){if(!ok)throw new InvalidOperationException(text);r.Checks.Add("PASS "+text);Console.WriteLine("PASS "+text);}
  static string Value(IModelDoc2 d,string key){string raw,value;d.Extension.CustomPropertyManager[""].Get2(key,out raw,out value);return value;}
  public static LengthTestReport Run(object application,string workspace,string workflowRoot,string template,string hash) {
   ISldWorks app=(ISldWorks)application;IModelDoc2 before=app.ActiveDoc as IModelDoc2;bool dirty=before!=null&&before.GetSaveFlag();string folder=Path.Combine(workflowRoot,"tests","length-runs",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));var r=new LengthTestReport{OutputFolder=folder};var session=new SpsSession(app,workspace,folder);IModelDoc2 d=null;
   try {
    d=session.Copy(template,"LengthPrecisionTest-01.SLDPRT","Default",hash);double length=1.9375*.0254;session.SetPipeLength(d,length,.0889,.082804);
    d.Extension.CustomPropertyManager["Default"].Add3("Stock Length",30,"1.9375 in",2);
    var known=new Dictionary<string,string>{{"Customer","SYNTHETIC FORMAT TEST"},{"Description","PIPE 3 INCH, 1.9375 IN STOCK LENGTH"},{"Part Description","PIPE, \"D1@Boss-Extrude1\" IN LONG"},{"Stock Length","1.9375 in"},{"Max Modeled Length","1.9375 in"}};
    session.ApplyProperties(d,known,true,false,"");d.EditRebuild3();r.Quantity=Value(d,"MaterialQty");r.Description=Value(d,"Part Description");
    Assert(r,r.Quantity=="1.938","Linked MaterialQty evaluates to 1.938, not 1.9375 or 1.94");
    Assert(r,r.Description.Contains("1.938")&&!r.Description.Contains("D1@"),"Linked description length evaluates with three decimals");
    Assert(r,Value(d,"Description").Contains("1.938")&&Value(d,"Stock Length")=="1.938 in"&&Value(d,"Max Modeled Length")=="1.938 in","Literal length properties and descriptive length text are rounded");
    string raw,evaluated;d.Extension.CustomPropertyManager["Default"].Get2("Stock Length",out raw,out evaluated);Assert(r,evaluated=="1.938 in","Configuration-specific length override uses the same formatting");
    Assert(r,Math.Abs(((IDimension)d.Parameter("D1@Boss-Extrude1")).SystemValue-length)<1e-12,"Native stock-length dimension retains full precision");
    ((IDimension)d.Parameter("D1@Boss-Extrude1")).SystemValue=1.23456*.0254;d.EditRebuild3();Assert(r,Value(d,"MaterialQty")=="1.235","Length property remains linked and updates after a native length change");
    ((IDimension)d.Parameter("D1@Boss-Extrude1")).SystemValue=length;d.EditRebuild3();session.CompletePart(d,new PartExpectations());d=null;
    Assert(r,SpsSession.Hash(template)==hash,"Library blank unchanged");r.Status="passed";
   }catch(Exception error){r.Status="failed";r.Error=error.ToString();Console.WriteLine(r.Error);}
   finally{if(d!=null){try{session.SaveChecked(d);session.CloseSaved(d);}catch(Exception){}}if(before!=null){if(before.GetSaveFlag()!=dirty){r.Status="failed";r.Error="Original active document save state changed";}int e=0;app.ActivateDoc3(before.GetTitle(),false,0,ref e);}}
   return r;
  }
 }
}
