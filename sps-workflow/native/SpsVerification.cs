namespace SpsWorkflow {
 using System;
 using System.IO;
 using System.Collections.Generic;
 using System.Text.RegularExpressions;
 using System.Web.Script.Serialization;
 using SolidWorks.Interop.sldworks;

 public static class MetadataPolicy {
  public static Dictionary<string,string> Resolve(Dictionary<string,string> known,Dictionary<string,string> inherited,string filename,bool pipe,bool modified,string mappingEvidence) {
   if(known==null||inherited==null)throw new ArgumentException("Known/inherited property dictionaries required");
   var values=new Dictionary<string,string>(known);string value;
   // Only pipe descriptions with native length links are safe to inherit. Never
   // borrow a customer, job, drafter, revision or date from a library template.
   if(pipe)foreach(string key in new string[]{"Description","Part Description"})if((!values.TryGetValue(key,out value)||string.IsNullOrWhiteSpace(value))&&inherited.TryGetValue(key,out value)&&value!=null&&(value.Contains("D1@Boss-Extrude1")||value.Contains("RD1@Annotations")))values[key]=value;
   foreach(string key in new string[]{"Description","Part Description"})if(values.TryGetValue(key,out value)&&value!=null){value=Regex.Replace(value,"SW-Material@[^\"]+",delegate(Match match){return "SW-Material@"+filename;});if(pipe)value=value.Replace("RD1@Annotations","D1@Boss-Extrude1");values[key]=PropertyLengthFormat.Description(value);}
   values["Number"]=Path.GetFileNameWithoutExtension(filename);values["Material"]="\"SW-Material@"+filename+"\"";
   if(!values.TryGetValue("MakeOrBuy",out value)||string.IsNullOrWhiteSpace(value))values["MakeOrBuy"]="M";
   if(pipe){inherited.TryGetValue("Material ID",out value);values["Material ID"]=value??"";values["MaterialQty"]="\"D1@Boss-Extrude1\"";}
   if(modified){if(values.TryGetValue("Material ID",out value)&&!string.IsNullOrWhiteSpace(value)){if(string.IsNullOrWhiteSpace(mappingEvidence)||!File.Exists(mappingEvidence))throw new InvalidOperationException("Verified modified-stock mapping evidence required");}else values["Material ID"]="";if(!values.TryGetValue("MaterialQty",out value)||string.IsNullOrWhiteSpace(value))values["MaterialQty"]="1";}
   foreach(string key in SpsSession.RequiredProperties)if(!values.ContainsKey(key))values[key]="";return values;
  }
 }
 public sealed class FeatureExpectation {
  public string Name,Type,ReferencedSketch;
  public double Radius;
  public double[] Start,Stop;
  public int ProfileEdges;
  public bool DoubleD;
 }
 public sealed class PartExpectations {
  public string Configuration,Material;
  public string PlanFile,PlanSha256,PartId;
  public bool CopedPipe;
  public Dictionary<string,double> Dimensions=new Dictionary<string,double>();
  public List<double> CylinderRadii=new List<double>();
  public List<FeatureExpectation> Features=new List<FeatureExpectation>();
 }
 public sealed class PropertyReading {
  public string Raw,Evaluated,ConfigurationRaw,ConfigurationEvaluated;
  public int Type;
 }
 public sealed class FeatureReading {
  public string Name,Type;
  public bool Suppressed,Warning;
  public int Error;
 }
 public sealed class PartVerification {
  public int ProtocolVersion=1;
  public string Status="checking",File,FileSha256,VerifiedUtc;
  public bool Saved,Closed;
  public PartSnapshot Snapshot;
  public PartExpectations Expected;
  public Dictionary<string,PropertyReading> Properties=new Dictionary<string,PropertyReading>();
  public List<FeatureReading> Features=new List<FeatureReading>();
  public List<string> Checks=new List<string>(),Warnings=new List<string>();
 }
 public static class EvidenceJson {
  public static void WriteNew(string path,object value){string text=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(value);using(var writer=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)))writer.WriteLine(text);}
 }
 public sealed partial class SpsSession {
  void ReadFeatures(IFeature feature,PartVerification report,HashSet<string> seen) {
   if(feature==null||!seen.Add(feature.Name))return;bool warning;int error=feature.GetErrorCode2(out warning);bool suppressed=feature.IsSuppressed();
   report.Features.Add(new FeatureReading{Name=feature.Name,Type=FeatureType(feature),Error=error,Warning=warning,Suppressed=suppressed});
   Check(suppressed||error==0,"Active feature error "+feature.Name+": "+error);if(warning&&!suppressed)report.Warnings.Add("Feature warning: "+feature.Name);
   for(IFeature child=feature.GetFirstSubFeature() as IFeature;child!=null;child=child.GetNextSubFeature() as IFeature)ReadFeatures(child,report,seen);
  }
  public PartVerification VerifyAndSave(IModelDoc2 d,PartExpectations expected) {
   Own(d);Check(expected!=null,"Part expectations required");if(expected.CopedPipe||Property(d,"Length Description Basis")=="Max Modeled Length")RefreshCopedPipeDescription(d);var result=new PartVerification{File=d.GetPathName(),Expected=expected,Snapshot=Snapshot(d),VerifiedUtc=DateTime.UtcNow.ToString("o")};
   if(!string.IsNullOrEmpty(expected.PlanFile))Check(Hash(expected.PlanFile)==expected.PlanSha256,"Plan changed during part build; revalidate affected work");
   if(!string.IsNullOrEmpty(expected.Configuration))Check(result.Snapshot.Configuration==expected.Configuration,"Unexpected active configuration");
   if(!string.IsNullOrEmpty(expected.Material))Check(result.Snapshot.Material==expected.Material,"Unexpected actual material");
   var seen=new HashSet<string>();for(IFeature f=d.FirstFeature() as IFeature;f!=null;f=f.GetNextFeature() as IFeature)ReadFeatures(f,result,seen);
   foreach(var pair in expected.Dimensions)CheckDimension(d,pair.Key,pair.Value);
   foreach(double radius in expected.CylinderRadii){bool found=false;foreach(double actual in result.Snapshot.CylinderRadii)if(Math.Abs(actual-radius)<1e-7)found=true;Check(found,"Expected actual cylindrical radius absent: "+radius);}
   foreach(FeatureExpectation e in expected.Features){IFeature f=Find(d,e.Name);Check(f!=null&&!f.IsSuppressed()&&FeatureType(f)==e.Type,"Expected active feature/type absent: "+e.Name);List<IFeature> refs=ReferencedSketches(f);
    if(!string.IsNullOrEmpty(e.ReferencedSketch)){bool found=false;foreach(IFeature sketch in refs)if(sketch.Name==e.ReferencedSketch)found=true;Check(found,"Expected sketch is not referenced by active feature: "+e.Name);}
    if(e.Radius>0){CircleProfile[] circles=ActiveCircleProfiles(d,e.Name);Check(circles.Length==2,"Expected two active full-circle profiles: "+e.Name);bool start=false,stop=false;foreach(CircleProfile p in circles){Check(Math.Abs(p.Radius-e.Radius)<1e-8,"Active opening radius differs from plan");if(Norm(Sub(p.Center,e.Start))<1e-7)start=true;if(Norm(Sub(p.Center,e.Stop))<1e-7)stop=true;}Check(start&&stop,"Active opening endpoints differ from plan");}
    if(e.ProfileEdges>0){int count=0;foreach(IFeature sketch in refs){ISketch sk=sketch.GetSpecificFeature2() as ISketch;if(sk==null)continue;object[] segments=sk.GetSketchSegments() as object[];if(segments!=null)foreach(ISketchSegment segment in segments)if(!segment.ConstructionGeometry)count++;}Check(count==e.ProfileEdges,"Active profile edge count differs: "+e.Name);}
    if(e.DoubleD){int lines=0,corners=0,large=0;foreach(IFeature sketch in refs){ISketch sk=sketch.GetSpecificFeature2() as ISketch;object[] segments=sk==null?null:sk.GetSketchSegments() as object[];if(segments==null)continue;foreach(ISketchSegment segment in segments){if(segment.ConstructionGeometry)continue;ISketchArc arc=segment as ISketchArc;if(segment is ISketchLine)lines++;else if(arc!=null&&Math.Abs(arc.GetRadius()-.001524)<1e-8)corners++;else if(arc!=null&&Math.Abs(arc.GetRadius()-.019558)<1e-8)large++;else throw new InvalidOperationException("Unexpected active double-D geometry");}}Check(lines==2&&corners==4&&large==2,"Active double-D topology/radii differ from approved block");}
    result.Checks.Add("Active feature geometry: "+e.Name);
   }
   ICustomPropertyManager pm=d.Extension.CustomPropertyManager[""],cfg=d.Extension.CustomPropertyManager[result.Snapshot.Configuration];var overrides=new HashSet<string>();object[] cn=cfg.GetNames() as object[];if(cn!=null)foreach(string key in cn)overrides.Add(key);
   foreach(string key in RequiredProperties){string raw,value;bool resolved,linked;int type=pm.GetType2(key);Check(type==30,"Required Text property missing: "+key);pm.Get6(key,false,out raw,out value,out resolved,out linked);var reading=new PropertyReading{Raw=raw??"",Evaluated=value??"",Type=type};
    if(overrides.Contains(key)){cfg.Get6(key,false,out raw,out value,out resolved,out linked);reading.ConfigurationRaw=raw??"";reading.ConfigurationEvaluated=value??"";Check(cfg.GetType2(key)==30&&reading.ConfigurationRaw==reading.Raw,"Configuration property overrides document value: "+key);}
    if(string.IsNullOrWhiteSpace(reading.Raw))result.Warnings.Add("Unknown property: "+key);
    if(reading.Raw.Contains("@"))Check(!string.IsNullOrWhiteSpace(reading.Evaluated)&&!reading.Evaluated.Contains("SW-Material@")&&!reading.Evaluated.Contains("D1@")&&!reading.Evaluated.Contains("RD1@"),"Unresolved linked property: "+key);
    if(key=="Material"||key=="MaterialQty")Check(!string.IsNullOrWhiteSpace(reading.Evaluated),"Required material/quantity value absent: "+key);
    if(key=="MaterialQty"&&(reading.Raw.Contains("D1@Boss-Extrude1")||reading.Raw.Contains("RD1@Annotations")))Check(!PropertyLengthFormat.ExcessDecimals(reading.Evaluated),"Length property exceeds three decimal places: "+key);
    result.Properties[key]=reading;
   }
   foreach(string key in PropertyLengthFormat.Names){var reading=new PropertyReading();bool found=false;ICustomPropertyManager[] managers={pm,cfg};for(int i=0;i<managers.Length;i++){ICustomPropertyManager manager=managers[i];if(manager.GetType2(key)!=30)continue;string raw,value;bool resolved,linked;manager.Get6(key,false,out raw,out value,out resolved,out linked);Check(!PropertyLengthFormat.ExcessDecimals(value),"Length property exceeds three decimal places: "+key);if(i==0){reading.Raw=raw;reading.Evaluated=value;reading.Type=30;}else{reading.ConfigurationRaw=raw;reading.ConfigurationEvaluated=value;}found=true;}if(found)result.Properties[key]=reading;}
   result.Checks.Add("One valid solid; native active-feature errors checked recursively");result.Checks.Add("Expected native dimensions, configuration and cylindrical radii");result.Checks.Add("All 14 Text properties, links and configuration overrides");
   int errorCode=0,warnings=0;Check(d.Save3(1,ref errorCode,ref warnings)&&errorCode==0,"Save failed: "+errorCode);if(warnings!=0)result.Warnings.Add("SolidWorks save warning code: "+warnings);result.Saved=true;result.Status="passed";return result;
  }
  public PartVerification CompletePart(IModelDoc2 d,PartExpectations expected) {
   PartVerification result=VerifyAndSave(d,expected);CloseSaved(d);result.Closed=app.GetOpenDocumentByName(result.File)==null;Check(result.Closed,"Completed component remained open");result.FileSha256=Hash(result.File);EvidenceJson.WriteNew(result.File+".verification.json",result);return result;
  }
 }
}
