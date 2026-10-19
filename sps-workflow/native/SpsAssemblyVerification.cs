namespace SpsWorkflow {
 using System;
 using System.IO;
 using System.Collections.Generic;
 using SolidWorks.Interop.sldworks;
 public sealed class PlannedOccurrence {
  public string File,Configuration,FileSha256;
  public double[] Transform;
 }
 public sealed class AssemblyVerification {
  public int ProtocolVersion=1;
  public string Status="checking",File,FileSha256,VerifiedUtc;
  public string PlanSha256;
  public bool Saved,MainAssemblyOpen;
  public List<string> Checks=new List<string>();
  public List<PlannedOccurrence> Components=new List<PlannedOccurrence>();
 }
 public static class AssemblyVerifier {
  static void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);}
  public static AssemblyVerification Verify(object application,object assembly,PlannedOccurrence[] expected,string[] excludedReferenceFiles,string receiptPath,string reviewedPlanSha256=null) {
   ISldWorks app=(ISldWorks)application;IModelDoc2 d=(IModelDoc2)assembly;Check(d!=null&&d.GetType()==2&&File.Exists(d.GetPathName()),"Saved native assembly required");Check(!d.GetSaveFlag(),"Assembly has unsaved changes; save/rebuild before final verification");Check(expected!=null&&expected.Length>0,"Expected ordered occurrences required");
   var references=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(string path in excludedReferenceFiles??new string[0])references.Add(Path.GetFullPath(path));
   var report=new AssemblyVerification{File=d.GetPathName(),PlanSha256=reviewedPlanSha256,VerifiedUtc=DateTime.UtcNow.ToString("o"),Saved=true,MainAssemblyOpen=app.GetOpenDocumentByName(d.GetPathName())!=null};
   var actual=new List<IComponent2>();for(IFeature f=d.FirstFeature() as IFeature;f!=null;f=f.GetNextFeature() as IFeature){bool warning;int error=f.GetErrorCode2(out warning);Check(error==0||f.IsSuppressed(),"Active assembly feature error: "+f.Name);if(FeatureType(f)!="Reference")continue;IComponent2 c=f.GetSpecificFeature2() as IComponent2;if(c==null)continue;string path=c.GetPathName();if(references.Contains(Path.GetFullPath(path))){Check(c.IsSuppressed()&&c.ExcludeFromBOM,"Customer reference must be suppressed and excluded from BOM");continue;}actual.Add(c);}
   Check(actual.Count==expected.Length,"Manufactured occurrence count differs from plan");var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   for(int i=0;i<expected.Length;i++){PlannedOccurrence p=expected[i];IComponent2 c=actual[i];string path=c.GetPathName();Check(string.Equals(Path.GetFullPath(path),Path.GetFullPath(p.File),StringComparison.OrdinalIgnoreCase),"FeatureManager/BOM source order differs at occurrence "+i);Check(!c.IsSuppressed()&&!c.ExcludeFromBOM&&c.ReferencedConfiguration==p.Configuration,"Suppression, BOM or configuration mismatch: "+path);Check(c.GetModelDoc2()!=null,"Unresolved component: "+path);Check(File.Exists(path),"Missing native reference: "+path);
    if(!hashes.ContainsKey(path))hashes[path]=SpsSession.Hash(path);Check(hashes[path]==p.FileSha256,"Component file differs from verified evidence: "+path);double[] transform=c.Transform2==null?null:(double[])c.Transform2.ArrayData;Check(transform!=null&&p.Transform!=null&&p.Transform.Length==16,"Missing transform: "+path);for(int k=0;k<16;k++)Check(!double.IsNaN(p.Transform[k])&&!double.IsInfinity(p.Transform[k])&&Math.Abs(transform[k]-p.Transform[k])<1e-7,"Component placement differs from reviewed plan: "+path);
    report.Components.Add(new PlannedOccurrence{File=path,Configuration=c.ReferencedConfiguration,FileSha256=hashes[path],Transform=transform});
   }
   report.Checks.Add("Ordered native occurrences, configurations, suppression and BOM eligibility");report.Checks.Add("All native file references and hashes");report.Checks.Add("All 16 transform values for each reviewed occurrence");report.Checks.Add("Saved assembly remains open; active assembly feature errors checked");report.FileSha256=SpsSession.Hash(report.File);report.Status="passed";EvidenceJson.WriteNew(receiptPath,report);return report;
  }
  static string FeatureType(IFeature feature){return SpsSession.FeatureType(feature);}
 }
}
