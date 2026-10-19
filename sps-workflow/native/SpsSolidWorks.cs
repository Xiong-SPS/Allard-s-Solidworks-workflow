using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using SolidWorks.Interop.sldworks;

namespace SpsWorkflow {
 public sealed class CircleProfile {
  public string Feature, Sketch;
  public double[] Center, Normal;
  public double Radius;
 }
 public sealed class PartSnapshot {
  public string File, Configuration, Material, MaterialId;
  public int SolidCount, BodyCheck;
  public double[] Bounds;
  public List<double> CylinderRadii = new List<double>();
 }
 public sealed class GrooveSpec {
  public string System, Method, Source, Revision;
  public bool ReviewedForJob;
  public double OD, A, B, C, MinimumWall, BlendRadius;
 }
 public sealed class DimensionEdit {
  public string Name;
  public double Expected, Target;
 }
 public sealed class DoubleDProfile {
  internal double[][] Edges;
  public string SourceFile, Sha256;
  public double Overall=0.039116, AcrossFlats=0.024892, CornerRadius=0.001524;
 }
 public interface IBoreRegion { bool Contains(double[] point); }
 public sealed class CylinderBore : IBoreRegion {
  readonly double[] origin, axis; readonly double radius, minimum, maximum;
  public CylinderBore(double[] origin, double[] axis, double innerRadius,double axialMinimum,double axialMaximum) {
   this.origin=origin;this.axis=SpsSession.Unit(axis);radius=innerRadius;
   minimum=axialMinimum;maximum=axialMaximum;
   if(radius<=0||maximum<=minimum)throw new ArgumentException("Actual parent inside radius and axial bounds required");
  }
  public bool Contains(double[] point) {
   double[] q=SpsSession.Sub(point,origin);double station=SpsSession.Dot(q,axis);return station>=minimum-1e-7&&station<=maximum+1e-7&&SpsSession.Norm(SpsSession.Sub(q,SpsSession.Scale(axis,station))) < radius-1e-7;
  }
 }
 public sealed class TorusBore : IBoreRegion {
  readonly double[] center, normal,radial,tangent;readonly double major, minor,angle;
  public TorusBore(double[] center,double[] radialStart,double[] tangentStart,double majorRadius,double innerMinorRadius,double angleRadians) {
   this.center=center;radial=SpsSession.Unit(radialStart);tangent=SpsSession.Unit(tangentStart);normal=SpsSession.Unit(SpsSession.Cross(radial,tangent));major=majorRadius;minor=innerMinorRadius;angle=angleRadians;
   if(major<=minor||minor<=0||angle<=0||angle>Math.PI*2||Math.Abs(SpsSession.Dot(radial,tangent))>1e-7)throw new ArgumentException("Invalid bounded torus bore");
  }
  public bool Contains(double[] point) {
   double[] q=SpsSession.Sub(point,center);double h=SpsSession.Dot(q,normal);
   double r=Math.Sqrt(Math.Max(0,SpsSession.Dot(q,q)-h*h));double theta=Math.Atan2(SpsSession.Dot(q,tangent),SpsSession.Dot(q,radial));if(theta<0&&angle>=2*Math.PI-1e-7)theta+=2*Math.PI;
   return theta>=-1e-7&&theta<=angle+1e-7&&Math.Sqrt((r-major)*(r-major)+h*h)<minor-1e-7;
  }
 }
 public sealed class UnionBore : IBoreRegion {
  readonly IBoreRegion[] regions;public UnionBore(params IBoreRegion[] regions){this.regions=regions;}
  public bool Contains(double[] p){foreach(IBoreRegion r in regions)if(r.Contains(p))return true;return false;}
 }

 // One session per operation. Only files created by this session can be edited/saved.
 public sealed partial class SpsSession {
  readonly ISldWorks app; readonly IMathUtility math; readonly string root;
  readonly HashSet<string> owned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  readonly Dictionary<string,DoubleDProfile> blockCache=new Dictionary<string,DoubleDProfile>(StringComparer.OrdinalIgnoreCase);
  public static readonly string[] RequiredProperties={"Customer","Description","Job Number","Item Number","DrawnBy","Part Description","Material","Number","MaterialQty","Material ID","Drawn By","Drawn Date","MakeOrBuy","Revision"};
  public SpsSession(object application,string workspace,string newOutputFolder):this(application,workspace,newOutputFolder,false) {}
  SpsSession(object application,string workspace,string newOutputFolder,bool planningFolder) {
   app=(ISldWorks)application;math=app.GetMathUtility() as IMathUtility;
   workspace=Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);
   root=Path.GetFullPath(newOutputFolder).TrimEnd(Path.DirectorySeparatorChar);
   if(!root.StartsWith(workspace+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Output must be inside the project workspace");
   if(File.Exists(root)||(!planningFolder&&Directory.Exists(root)))throw new IOException("Output folder must be new; existing jobs cannot be overwritten");
   if(planningFolder){Check(Directory.Exists(root),"Planned job folder missing");foreach(string file in Directory.GetFiles(root)){string ext=Path.GetExtension(file);Check(!ext.Equals(".SLDPRT",StringComparison.OrdinalIgnoreCase)&&!ext.Equals(".SLDASM",StringComparison.OrdinalIgnoreCase)&&!ext.Equals(".SLDDRW",StringComparison.OrdinalIgnoreCase),"Planned job folder already contains native models; inspect/resume deliberately");}}
   Directory.CreateDirectory(root);
  }
  public static SpsSession ForPlannedJob(object application,string workspace,string jobFolder,string reviewedPlan,string expectedPlanSha256) {
   string root=Path.GetFullPath(jobFolder).TrimEnd(Path.DirectorySeparatorChar);string plan=Path.GetFullPath(reviewedPlan);
   Check(string.Equals(Path.GetDirectoryName(plan),root,StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(plan).Equals(".json",StringComparison.OrdinalIgnoreCase),"Reviewed JSON plan must be directly inside the job folder");
   Check(!string.IsNullOrEmpty(expectedPlanSha256)&&Hash(plan)==expectedPlanSha256,"Plan changed since review; check it again before modeling");
   return new SpsSession(application,workspace,root,true);
  }
  public static double[] V(double x,double y,double z){return new double[]{x,y,z};}
  public static double[] Add(double[] a,double[] b){return V(a[0]+b[0],a[1]+b[1],a[2]+b[2]);}
  public static double[] Sub(double[] a,double[] b){return Add(a,Scale(b,-1));}
  public static double[] Scale(double[] a,double s){return V(a[0]*s,a[1]*s,a[2]*s);}
  public static double Dot(double[] a,double[] b){return a[0]*b[0]+a[1]*b[1]+a[2]*b[2];}
  public static double Norm(double[] a){return Math.Sqrt(Dot(a,a));}
  public static double[] Unit(double[] a){double n=Norm(a);if(n<1e-12||double.IsNaN(n)||double.IsInfinity(n))throw new ArgumentException("Invalid direction");return Scale(a,1/n);}
  public static double[] Cross(double[] a,double[] b){return V(a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]);}
  public static string Hash(string path){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
  public static string FeatureType(IFeature f){string t=f.GetTypeName2();return t=="ICE"?f.GetTypeName():t;}
  static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  void Own(IModelDoc2 d){Check(d!=null&&owned.Contains(Path.GetFullPath(d.GetPathName())),"Refusing to edit an existing/library document");}
  void Activate(IModelDoc2 d){if(app.ActiveDoc==d)return;int error=0;app.ActivateDoc3(d.GetTitle(),false,0,ref error);Check(app.ActiveDoc==d,"Could not activate owned document");}
  static IFeature Find(IModelDoc2 d,string name){return ((IPartDoc)d).FeatureByName(name) as IFeature;}
  static List<IFeature> ReferencedSketches(IFeature feature) {
   var sketches=new List<IFeature>();var seen=new HashSet<string>();object[] parents=feature.GetParents() as object[];
   if(parents!=null)foreach(IFeature parent in parents)if(parent!=null&&(parent.GetTypeName2()=="ProfileFeature"||parent.GetTypeName2()=="3DProfileFeature")&&parent.GetSpecificFeature2() is ISketch&&seen.Add(parent.Name))sketches.Add(parent);
   // Extrusions commonly absorb their sketch as a subfeature instead of listing it as a parent.
   for(IFeature child=feature.GetFirstSubFeature() as IFeature;child!=null;child=child.GetNextSubFeature() as IFeature)if((child.GetTypeName2()=="ProfileFeature"||child.GetTypeName2()=="3DProfileFeature")&&child.GetSpecificFeature2() is ISketch&&seen.Add(child.Name))sketches.Add(child);
   return sketches;
  }
  void Free(IModelDoc2 d,params string[] names){Own(d);foreach(string n in names)Check(Find(d,n)==null,"Feature/sketch name already exists: "+n+". Use a new prefix or explicitly replace the active feature.");}
  static void Name(IFeature f,string name){Check(f!=null,"Feature creation failed: "+name);f.Name=name;Check(f.Name==name,"SolidWorks rejected feature name: "+name);}
  static void Prop(IModelDoc2 d,string name,string value){d.Extension.CustomPropertyManager[""].Add3(name,30,value??"",2);}
  public static string Property(IModelDoc2 d,string name){string raw,value;d.Extension.CustomPropertyManager[""].Get2(name,out raw,out value);return raw??"";}
  static IBody2 Body(IModelDoc2 d){object[] bodies=((IPartDoc)d).GetBodies2(0,false) as object[];Check(bodies!=null&&bodies.Length==1,"Expected one production solid: "+d.GetTitle());IBody2 b=(IBody2)bodies[0];Check(b.Check2()==0,"Invalid solid: "+d.GetTitle());return b;}
  public IModelDoc2 Copy(string template,string filename,string configuration,string expectedSha256) {
   Check(Path.GetFileName(filename)==filename&&filename.EndsWith(".SLDPRT",StringComparison.OrdinalIgnoreCase),"Use a simple native part filename");
   Check(!string.IsNullOrEmpty(expectedSha256)&&Hash(template)==expectedSha256,"Template missing/changed since verification; remeasure first");
   string target=Path.Combine(root,filename);using(var input=new FileStream(template,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);Check(Hash(target)==expectedSha256,"Source changed during copy");File.SetAttributes(target,FileAttributes.Normal);
   int e=0,w=0;IModelDoc2 d=app.OpenDoc6(target,1,1,"",ref e,ref w) as IModelDoc2;
   Check(d!=null&&e==0,"Open copied part failed: "+e);owned.Add(Path.GetFullPath(target));
   d.ShowConfiguration2(configuration);Check(d.ConfigurationManager.ActiveConfiguration.Name==configuration,"Configuration missing: "+configuration);
   Prop(d,"Template File",Path.GetFullPath(template));Prop(d,"Template SHA256",expectedSha256);Prop(d,"Template Stock Identifier",Path.GetFileNameWithoutExtension(template));
   Activate(d);return d;
  }
  public PartSnapshot Snapshot(IModelDoc2 d) {
   IBody2 b=Body(d);PartSnapshot s=new PartSnapshot();s.File=d.GetPathName();s.Configuration=d.ConfigurationManager.ActiveConfiguration.Name;s.SolidCount=1;s.BodyCheck=0;
   string db;s.Material=((IPartDoc)d).GetMaterialPropertyName2(s.Configuration,out db);s.MaterialId=Property(d,"Material ID");s.Bounds=new double[6];
   for(int axis=0;axis<3;axis++){double x,y,z;double[] dir=V(0,0,0);dir[axis]=1;b.GetExtremePoint(dir[0],dir[1],dir[2],out x,out y,out z);s.Bounds[axis+3]=new double[]{x,y,z}[axis];b.GetExtremePoint(-dir[0],-dir[1],-dir[2],out x,out y,out z);s.Bounds[axis]=new double[]{x,y,z}[axis];}
   foreach(IFace2 f in (object[])b.GetFaces()){ISurface surface=f.GetSurface() as ISurface;if(surface.IsCylinder()){double r=((double[])surface.CylinderParams)[6];bool found=false;foreach(double old in s.CylinderRadii)if(Math.Abs(old-r)<1e-8)found=true;if(!found)s.CylinderRadii.Add(r);}}
   s.CylinderRadii.Sort();return s;
  }
  public PartSnapshot SetPipeLength(IModelDoc2 d,double length,double expectedOD,double expectedID) {
   Own(d);Activate(d);Check(length>0&&expectedOD>expectedID&&expectedID>0,"Invalid pipe dimensions");PartSnapshot before=Snapshot(d);
   Check(before.CylinderRadii.Count==2&&Math.Abs(before.CylinderRadii[0]*2-expectedID)<1e-8&&Math.Abs(before.CylinderRadii[1]*2-expectedOD)<1e-8,"Template is not the expected plain pipe blank");
   IFeature extrusion=Find(d,"Boss-Extrude1");Check(extrusion!=null,"Verified length feature missing");int circles=0;
   foreach(IFeature parent in ReferencedSketches(extrusion)){ISketch sk=parent.GetSpecificFeature2() as ISketch;Check(sk!=null,"Referenced pipe sketch unavailable: "+parent.Name);object[] segments=sk.GetSketchSegments() as object[];Check(segments!=null,"Referenced pipe sketch is empty: "+parent.Name);foreach(ISketchSegment seg in segments){if(seg.ConstructionGeometry)continue;ISketchArc arc=seg as ISketchArc;Check(arc!=null&&arc.IsCircle()==1,"Pipe profile must use full circles");circles++;}}
   Check(circles==2,"Expected two full circles on the active pipe extrusion");IDimension dim=d.Parameter("D1@Boss-Extrude1") as IDimension;Check(dim!=null,"Length dimension missing");dim.SystemValue=length;Check(d.EditRebuild3(),"Pipe rebuild failed");PartSnapshot after=Snapshot(d);
   Check(Math.Abs(after.Bounds[0])<1e-7&&Math.Abs(after.Bounds[3]-length)<1e-7,"Total length differs from target; check reverse extrusion and datum");
   Check(before.Material==after.Material&&before.MaterialId==after.MaterialId,"Pipe material/stock changed");
   Check(after.CylinderRadii.Count==2&&Math.Abs(after.CylinderRadii[0]*2-expectedID)<1e-8&&Math.Abs(after.CylinderRadii[1]*2-expectedOD)<1e-8,"Pipe diameters changed");return after;
  }
  public PartSnapshot EditExistingDimensions(IModelDoc2 d,DimensionEdit[] edits,Dictionary<string,double> preserved) {
   Own(d);Activate(d);Check(edits!=null&&edits.Length>0&&preserved!=null&&preserved.Count>0,"Supply native dimension edits and protected dimensions");
   var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);PartSnapshot before=Snapshot(d);
   foreach(DimensionEdit edit in edits){Check(edit!=null&&!string.IsNullOrWhiteSpace(edit.Name)&&names.Add(edit.Name),"Duplicate/missing dimension edit");Check(FinitePositive(edit.Expected)&&FinitePositive(edit.Target),"Dimension values must be finite positive SI values");CheckDimension(d,edit.Name,edit.Expected);}
   foreach(var pair in preserved){Check(!names.Contains(pair.Key)&&FinitePositive(pair.Value),"Protected dimension overlaps an edit or is invalid");CheckDimension(d,pair.Key,pair.Value);}
   // Validate every old value before editing any dimension. Preserve the native tree.
   foreach(DimensionEdit edit in edits)((IDimension)d.Parameter(edit.Name)).SystemValue=edit.Target;
   Check(d.EditRebuild3(),"Existing-dimension rebuild failed");foreach(DimensionEdit edit in edits)CheckDimension(d,edit.Name,edit.Target);
   foreach(var pair in preserved)CheckDimension(d,pair.Key,pair.Value);PartSnapshot after=Snapshot(d);
   Check(before.Material==after.Material&&before.MaterialId==after.MaterialId,"Dimension edit changed material or stock metadata");return after;
  }
  static bool FinitePositive(double value){return value>0&&!double.IsNaN(value)&&!double.IsInfinity(value);}
  static void CheckDimension(IModelDoc2 d,string name,double expected){IDimension dimension=d.Parameter(name) as IDimension;Check(dimension!=null,"Native dimension missing: "+name);Check(Math.Abs(dimension.SystemValue-expected)<1e-8,"Native dimension differs from reviewed value: "+name);}
  static void CheckElbowMaterialGeometry(IModelDoc2 d,double major,double outside,double inside) {
   bool outer=false,inner=false;foreach(IFace2 face in (object[])Body(d).GetFaces()){ISurface surface=face.GetSurface() as ISurface;if(!surface.IsTorus())continue;double[] p=(double[])surface.TorusParams;if(Math.Abs(p[6]-major)>1e-8)continue;if(Math.Abs(p[7]-outside/2)<1e-8)outer=true;if(Math.Abs(p[7]-inside/2)<1e-8)inner=true;}
   Check(outer&&inner,"Actual elbow torus radius, OD or ID differs from reviewed library geometry");
  }
  public PartSnapshot SetElbowAngle(IModelDoc2 d,double originalAngle,double targetAngle,double bendRadius,double outsideDiameter,double insideDiameter) {
   Own(d);Check(FinitePositive(originalAngle)&&FinitePositive(targetAngle)&&originalAngle<=Math.PI&&targetAngle<=originalAngle,"Supported elbow edit trims an existing bend without increasing its base angle");
   Check(FinitePositive(bendRadius)&&outsideDiameter>insideDiameter&&FinitePositive(insideDiameter),"Actual elbow dimensions required");CheckElbowMaterialGeometry(d,bendRadius,outsideDiameter,insideDiameter);
   PartSnapshot result=EditExistingDimensions(d,new DimensionEdit[]{new DimensionEdit{Name="D1@Revolve1",Expected=originalAngle,Target=targetAngle}},new Dictionary<string,double>{{"D1@Sketch1",outsideDiameter},{"D2@Sketch1",bendRadius},{"D3@Sketch1",(outsideDiameter-insideDiameter)/2}});
   CheckElbowMaterialGeometry(d,bendRadius,outsideDiameter,insideDiameter);return result;
  }
  public string[] ApplyProperties(IModelDoc2 d,Dictionary<string,string> known,bool pipe,bool modifiedFitting,string customerSourceFile,string verifiedStockMappingEvidence=null,bool copedPipe=false) {
   Own(d);Check(!(pipe&&modifiedFitting),"Classify as pipe or modified fitting");string inheritedId=Property(d,"Material ID");string filename=Path.GetFileName(d.GetPathName());
   SetLengthPropertyPrecision(d);
   var inherited=new Dictionary<string,string>();foreach(string key in RequiredProperties)inherited[key]=Property(d,key);
   var values=MetadataPolicy.Resolve(known,inherited,filename,pipe,modifiedFitting,verifiedStockMappingEvidence);
   Check(!copedPipe||pipe,"Coped-pipe description requires a pipe");if(copedPipe){string basis=values["Part Description"];if(string.IsNullOrWhiteSpace(basis))basis=Property(d,"Pipe Description Template");Prop(d,"Pipe Description Template",basis);Prop(d,"Length Description Basis","Max Modeled Length");SetCopedDescriptionValues(d,values);}
   if(pipe){values["Material ID"]=inheritedId;values["MaterialQty"]="\"D1@Boss-Extrude1\"";}else if(modifiedFitting){
    if(values.ContainsKey("Material ID")&&!string.IsNullOrWhiteSpace(values["Material ID"])) {Check(!string.IsNullOrEmpty(verifiedStockMappingEvidence)&&File.Exists(verifiedStockMappingEvidence),"New fitting ERP mapping requires verified evidence; template identity is not a new stock mapping");Prop(d,"ERP Mapping Evidence",Path.GetFullPath(verifiedStockMappingEvidence));}else values["Material ID"]="";
    if(!values.ContainsKey("MaterialQty")||string.IsNullOrWhiteSpace(values["MaterialQty"]))values["MaterialQty"]="1";
   }
   var missing=new List<string>();ICustomPropertyManager pm=d.Extension.CustomPropertyManager[""];foreach(string key in RequiredProperties)pm.Delete2(key);
   foreach(string key in RequiredProperties){string value;values.TryGetValue(key,out value);Prop(d,key,value);if(string.IsNullOrWhiteSpace(value))missing.Add(key);}
   foreach(string key in PropertyLengthFormat.Names){string value;if(values.TryGetValue(key,out value))Prop(d,key,PropertyLengthFormat.Literal(value));else if(pm.GetType2(key)==30)Prop(d,key,PropertyLengthFormat.Literal(Property(d,key)));}
   // Configuration-level values must not override the new job's document values.
   ICustomPropertyManager cfg=d.Extension.CustomPropertyManager[d.ConfigurationManager.ActiveConfiguration.Name];object[] names=cfg.GetNames() as object[];
   if(names!=null)foreach(string key in names){if(Array.IndexOf(RequiredProperties,key)>=0)cfg.Add3(key,30,Property(d,key),2);else if(Array.IndexOf(PropertyLengthFormat.Names,key)>=0){string raw,evaluated;cfg.Get2(key,out raw,out evaluated);cfg.Add3(key,30,pm.GetType2(key)==30?Property(d,key):PropertyLengthFormat.Literal(raw),2);}}
   if(!string.IsNullOrEmpty(customerSourceFile)){Check(File.Exists(customerSourceFile),"Customer source file is missing");Prop(d,"Source File",Path.GetFileName(customerSourceFile));}else{Prop(d,"Source File","");missing.Add("Source File");}
   Prop(d,"Metadata Status",missing.Count==0?"Known required metadata complete":"Unknown/needs review: "+string.Join(", ",missing.ToArray()));return missing.ToArray();
  }
  public void SaveChecked(IModelDoc2 d) {
   VerifyAndSave(d,new PartExpectations());
  }
  public void CloseSaved(IModelDoc2 d){Own(d);Check(!d.GetSaveFlag(),"Unsaved owned model; save or retain it for diagnosis");app.CloseDoc(d.GetTitle());}

  IFeature Poly3(IModelDoc2 d,double[][] points,string name) {
   Free(d,name);Activate(d);d.ClearSelection2(true);d.SketchManager.Insert3DSketch(true);ISketch sk=d.SketchManager.ActiveSketch as ISketch;d.SketchManager.AddToDB=true;
   try{for(int i=0;i<points.Length;i++){double[] p=points[i],q=points[(i+1)%points.Length];Check(d.SketchManager.CreateLine(p[0],p[1],p[2],q[0],q[1],q[2])!=null,"Profile line failed");}}finally{d.SketchManager.AddToDB=false;}
   d.ClearSelection2(true);foreach(ISketchSegment seg in (object[])sk.GetSketchSegments())seg.Select2(true,0);d.SketchAddConstraints("sgFIXED");d.ClearSelection2(true);d.SketchManager.Insert3DSketch(true);IFeature f=sk as IFeature;Name(f,name);return f;
  }
  IFeature Plane(IModelDoc2 d,double[] origin,double[] u,double[] v,string prefix) {
   Free(d,prefix+"Points",prefix+"Plane");Activate(d);d.ClearSelection2(true);d.SketchManager.Insert3DSketch(true);ISketch sk=d.SketchManager.ActiveSketch as ISketch;var points=new List<ISketchPoint>();
   foreach(double[] p in new double[][]{origin,Add(origin,Scale(u,.1)),Add(origin,Scale(v,.1))})points.Add(d.SketchManager.CreatePoint(p[0],p[1],p[2]) as ISketchPoint);
   d.SketchManager.Insert3DSketch(true);Name(sk as IFeature,prefix+"Points");d.ClearSelection2(true);for(int i=0;i<3;i++)Check(points[i].Select2(true,i),"Plane point selection");IFeature plane=d.FeatureManager.InsertRefPlane(4,0,4,0,4,0) as IFeature;Name(plane,prefix+"Plane");return plane;
  }
  double[] SketchPoint(double[] p,IMathTransform tr){return (double[])((IMathPoint)((IMathPoint)math.CreatePoint(p)).MultiplyTransform((MathTransform)tr)).ArrayData;}
  IFeature Circle(IModelDoc2 d,double[] center,double[] u,double[] v,double radius,string prefix) {
   Free(d,prefix+"Profile");IFeature plane=Plane(d,center,u,v,prefix);d.ClearSelection2(true);plane.Select2(false,0);d.SketchManager.InsertSketch(true);ISketch sk=d.SketchManager.ActiveSketch as ISketch;double[] p=SketchPoint(center,sk.ModelToSketchTransform);
   Check(d.SketchManager.CreateCircleByRadius(p[0],p[1],0,radius)!=null,"Circle creation failed");d.SketchManager.InsertSketch(true);IFeature profile=sk as IFeature;Name(profile,prefix+"Profile");return profile;
  }
  IFeature Loft(IModelDoc2 d,IList<IFeature> profiles,IFeature guide,bool closed,string name) {
   Free(d,name);Activate(d);d.ClearSelection2(true);foreach(IFeature f in profiles)Check(f.Select2(true,1),"Loft profile selection failed");if(guide!=null)Check(guide.Select2(true,2),"Loft guide selection failed");
   IFeature cut=d.FeatureManager.InsertCutBlend(closed,true,false,1,0,0,false,0,0,0,true,true) as IFeature;Name(cut,name);Check(d.EditRebuild3(),"Loft rebuild failed");Body(d);return cut;
  }
  static void InteriorDisk(IBoreRegion bore,double[] stop,double[] u,double[] v,double radius) {
   Check(bore!=null&&bore.Contains(stop),"Cut stop must be verified inside the actual parent bore");
   for(int k=0;k<64;k++){double t=k*2*Math.PI/64;Check(bore.Contains(Add(stop,Add(Scale(u,radius*Math.Cos(t)),Scale(v,radius*Math.Sin(t))))),"Cut end profile reaches parent material; use a verified interior stop/region");}
  }
  public IFeature AddBranchOpening(IModelDoc2 d,double[] outsideStart,double[] interiorStop,double branchOD,double branchID,IBoreRegion parentBore,string name) {
   Own(d);Check(branchOD>branchID&&branchID>0,"Actual branch dimensions required");Free(d,name,name+"StartProfile",name+"EndProfile");double radius=(branchOD+branchID)/4;
   double[] direction=Unit(Sub(interiorStop,outsideStart)),u=Unit(Cross(direction,Math.Abs(direction[2])<.8?V(0,0,1):V(0,1,0))),v=Cross(direction,u);InteriorDisk(parentBore,interiorStop,u,v,radius);
   IFeature a=Circle(d,outsideStart,u,v,radius,name+"Start"),b=Circle(d,interiorStop,u,v,radius,name+"End");IFeature cut=Loft(d,new IFeature[]{a,b},null,false,name);
   CircleProfile[] actual=ActiveCircleProfiles(d,name);Check(actual.Length==2,"Active cut does not reference two circles");bool start=false,end=false;foreach(CircleProfile p in actual){Check(Math.Abs(p.Radius-radius)<1e-8,"Actual branch opening radius differs");if(Norm(Sub(p.Center,outsideStart))<1e-7)start=true;if(Norm(Sub(p.Center,interiorStop))<1e-7)end=true;}Check(start&&end,"Active cut endpoints differ from plan");return cut;
  }
  public CircleProfile[] ActiveCircleProfiles(IModelDoc2 d,string cutName) {
   IFeature cut=Find(d,cutName);Check(cut!=null&&!cut.IsSuppressed(),"Active cut missing/suppressed: "+cutName);Check(FeatureType(cut)=="BlendCut","Expected a native lofted cut");var result=new List<CircleProfile>();
   // Never search the tree by old sketch names. Read the parents of this cut.
   foreach(IFeature parent in ReferencedSketches(cut)){if(parent.GetTypeName2()!="ProfileFeature")continue;ISketch sk=parent.GetSpecificFeature2() as ISketch;Check(sk!=null,"Active profile sketch unavailable: "+parent.Name);IMathTransform modelToSketch=sk.ModelToSketchTransform;Check(modelToSketch!=null,"Active circle profile must be planar: "+parent.Name);IMathTransform tr=modelToSketch.Inverse() as IMathTransform;
    object[] segments=sk.GetSketchSegments() as object[];Check(segments!=null,"Active circle profile has no segments: "+parent.Name);foreach(ISketchSegment seg in segments){if(seg.ConstructionGeometry)continue;ISketchArc a=seg as ISketchArc;Check(a!=null&&a.IsCircle()==1,"Cut profile is not a full circle");ISketchPoint point=a.GetCenterPoint2() as ISketchPoint;IMathVector normal=math.CreateVector(V(0,0,1)) as IMathVector;normal=normal.MultiplyTransform((MathTransform)tr) as IMathVector;
     result.Add(new CircleProfile{Feature=cut.Name,Sketch=parent.Name,Center=SketchPoint(V(point.X,point.Y,point.Z),tr),Normal=(double[])normal.ArrayData,Radius=a.GetRadius()});}}
   return result.ToArray();
  }
  public IFeature AddMidWallCope(IModelDoc2 d,double stockLength,double middleRadius,double wall,double[][] guidePoints,string prefix) {
   Own(d);Check(guidePoints.Length>=17&&(guidePoints.Length-1)%4==0,"Closed guide needs four section stations");Check(Norm(Sub(guidePoints[0],guidePoints[guidePoints.Length-1]))<1e-7,"Cope guide must close");
   Check(Math.Abs(((IDimension)d.Parameter("D1@Boss-Extrude1")).SystemValue-stockLength)<1e-8,"Cope stock length differs from native blank");PartSnapshot stock=Snapshot(d);Check(stock.CylinderRadii.Count==2&&Math.Abs((stock.CylinderRadii[0]+stock.CylinderRadii[1])/2-middleRadius)<1e-8&&Math.Abs(stock.CylinderRadii[1]-stock.CylinderRadii[0]-wall)<1e-8,"Cope guide material dimensions differ from actual plain blank");
   Free(d,prefix,prefix+"Guide",prefix+"Section0",prefix+"Section1",prefix+"Section2",prefix+"Section3");
   foreach(double[] p in guidePoints){Check(p[0]>0&&p[0]<=stockLength,"Guide extends beyond specified stock");Check(Math.Abs(Math.Sqrt(p[1]*p[1]+p[2]*p[2])-middleRadius)<1e-7,"Guide is not on actual branch mid-wall");}
   Check(middleRadius>1.5*wall&&wall>0,"Invalid cope wall");Activate(d);d.ClearSelection2(true);d.SketchManager.Insert3DSketch(true);ISketch sk=d.SketchManager.ActiveSketch as ISketch;double[] xyz=new double[guidePoints.Length*3];for(int i=0;i<guidePoints.Length;i++)Array.Copy(guidePoints[i],0,xyz,i*3,3);
   Check(d.SketchManager.CreateSpline2(xyz,false)!=null,"Cope spline failed");d.SketchManager.Insert3DSketch(true);IFeature guide=sk as IFeature;Name(guide,prefix+"Guide");var profiles=new List<IFeature>();double low=middleRadius-1.5*wall,high=middleRadius+1.5*wall,back=stockLength+Math.Max(.0254,wall*4);
   for(int i=0;i<4;i++){double[] p=guidePoints[i*(guidePoints.Length-1)/4];double y=p[1]/middleRadius,z=p[2]/middleRadius;profiles.Add(Poly3(d,new double[][]{V(p[0],high*y,high*z),V(p[0],low*y,low*z),V(back,low*y,low*z),V(back,high*y,high*z)},prefix+"Section"+i));}
   IFeature cut=Loft(d,profiles,guide,true,prefix);Check(FeatureType(cut)=="BlendCut","Cope must remain a native loft");return cut;
  }
  public static double[][] PerpendicularCylinderCopeGuide(double stockLength,double actualBranchMidRadius,double actualParentOutsideRadius) {
   Check(actualParentOutsideRadius>actualBranchMidRadius&&actualBranchMidRadius>0&&stockLength>actualParentOutsideRadius,"Unsupported perpendicular cope dimensions");double[][] points=new double[129][];
   for(int i=0;i<128;i++){double theta=2*Math.PI*i/128,y=actualBranchMidRadius*Math.Cos(theta),z=actualBranchMidRadius*Math.Sin(theta);points[i]=V(stockLength-Math.Sqrt(actualParentOutsideRadius*actualParentOutsideRadius-z*z),y,z);}points[128]=(double[])points[0].Clone();return points;
  }

  public DoubleDProfile ReadStandard34Block(string partTemplate,string blockPath,string expectedHash) {
   Check(Path.GetFileName(blockPath).Equals("34 HALF-COUPLING.SLDBLK",StringComparison.OrdinalIgnoreCase)&&Hash(blockPath)==expectedHash,"Only the verified standard 3/4 block is supported by this helper");
   string cacheKey=Path.GetFullPath(blockPath)+"|"+expectedHash;DoubleDProfile cached;if(blockCache.TryGetValue(cacheKey,out cached))return CloneBlock(cached);
   IModelDoc2 scratch=app.NewDocument(partTemplate,0,0,0) as IModelDoc2;Check(scratch!=null,"Could not open scratch part template");
   try{scratch.Extension.SelectByID2("Front Plane","PLANE",0,0,0,false,0,null,0);scratch.SketchManager.InsertSketch(true);ISketchBlockDefinition def=scratch.SketchManager.MakeSketchBlockFromFile((MathPoint)math.CreatePoint(V(0,0,0)),blockPath,false,1,0) as ISketchBlockDefinition;Check(def!=null,"Approved block load failed");
    var edges=new List<double[]>();double[] center=null;int lines=0,large=0,corners=0;
    foreach(ISketchSegment seg in (object[])((ISketch)def.GetSketch()).GetSketchSegments()){if(seg.ConstructionGeometry)continue;ISketchLine line=seg as ISketchLine;ISketchArc arc=seg as ISketchArc;Check(line!=null||arc!=null,"Unexpected block entity");ISketchPoint p=line!=null?line.GetStartPoint2() as ISketchPoint:arc.GetStartPoint2() as ISketchPoint,q=line!=null?line.GetEndPoint2() as ISketchPoint:arc.GetEndPoint2() as ISketchPoint,c=arc==null?null:arc.GetCenterPoint2() as ISketchPoint;double radius=arc==null?0:arc.GetRadius();
     if(line!=null)lines++;else if(Math.Abs(radius-.019558)<1e-7){large++;double[] cc=V(c.X,c.Y,0);if(center==null)center=cc;else Check(Norm(Sub(center,cc))<1e-7,"Block main arc centers differ");}else if(Math.Abs(radius-.001524)<1e-7)corners++;else throw new InvalidOperationException("Unexpected block radius");
     edges.Add(new double[]{line!=null?0:1,p.X,p.Y,q.X,q.Y,c==null?0:c.X,c==null?0:c.Y,radius});}
    Check(edges.Count==8&&lines==2&&large==2&&corners==4&&center!=null,"Approved 3/4 block topology differs");
    var flatPositions=new List<double>();foreach(double[] edge in edges){edge[1]-=center[0];edge[2]-=center[1];edge[3]-=center[0];edge[4]-=center[1];if(edge[0]==1){edge[5]-=center[0];edge[6]-=center[1];}else{Check(Math.Abs(edge[2]-edge[4])<1e-7,"Block flats not parallel to reference long axis");flatPositions.Add(edge[2]);}}
    Check(Math.Abs(Math.Abs(flatPositions[0]-flatPositions[1])-.024892)<1e-7,"Block across-flats dimension differs");scratch.SketchManager.InsertSketch(true);cached=new DoubleDProfile{SourceFile=blockPath,Sha256=expectedHash,Edges=edges.ToArray()};blockCache.Add(cacheKey,cached);return CloneBlock(cached);
   }finally{app.CloseDoc(scratch.GetTitle());}
  }
  static DoubleDProfile CloneBlock(DoubleDProfile source){double[][] edges=new double[source.Edges.Length][];for(int i=0;i<edges.Length;i++)edges[i]=(double[])source.Edges[i].Clone();return new DoubleDProfile{SourceFile=source.SourceFile,Sha256=source.Sha256,Edges=edges};}
  public IFeature AddDoubleD(IModelDoc2 d,DoubleDProfile block,double[] weldCenter,double[] outward,double[] longAxis,double[] interiorStop,IBoreRegion parentBore,string name) {
   Own(d);Check(block!=null&&block.Edges!=null&&Hash(block.SourceFile)==block.Sha256,"Verified block required");Free(d,name,name+"Profile",name+"Plane",name+"Points");outward=Unit(outward);longAxis=Unit(Sub(longAxis,Scale(outward,Dot(longAxis,outward))));double[] v=Cross(outward,longAxis);
   Check(Norm(Cross(Sub(weldCenter,interiorStop),outward))<1e-7&&Dot(Sub(weldCenter,interiorStop),outward)>0,"Coupling stop must follow its inward centerline");InteriorDisk(parentBore,interiorStop,longAxis,v,block.Overall/2);
   double[] start=Add(weldCenter,Scale(outward,.0254));IFeature plane=Plane(d,start,longAxis,v,name);d.ClearSelection2(true);plane.Select2(false,0);d.SketchManager.InsertSketch(true);ISketch sk=d.SketchManager.ActiveSketch as ISketch;IMathTransform tr=sk.ModelToSketchTransform;d.SketchManager.AddToDB=true;
   try{foreach(double[] e in block.Edges){double[] a=Add(start,Add(Scale(longAxis,e[1]),Scale(v,e[2]))),b=Add(start,Add(Scale(longAxis,e[3]),Scale(v,e[4])));if(e[0]==0)Line(d,tr,a,b);else{double x=e[1]+e[3]-2*e[5],y=e[2]+e[4]-2*e[6],n=Math.Sqrt(x*x+y*y);double[] middle=Add(start,Add(Scale(longAxis,e[5]+e[7]*x/n),Scale(v,e[6]+e[7]*y/n)));Arc(d,tr,a,b,middle);}}}finally{d.SketchManager.AddToDB=false;}
   d.SketchManager.InsertSketch(true);IFeature profile=sk as IFeature;Name(profile,name+"Profile");IMathVector pn=math.CreateVector(V(0,0,1)) as IMathVector;pn=pn.MultiplyTransform((MathTransform)tr.Inverse()) as IMathVector;d.ClearSelection2(true);profile.Select2(false,0);
   IFeature cut=d.FeatureManager.FeatureCut3(true,false,Dot((double[])pn.ArrayData,outward)<0,0,0,Norm(Sub(start,interiorStop)),0,false,false,false,false,0,0,false,false,false,false,false,true,true,false,false,false,0,0,false) as IFeature;Name(cut,name);Check(d.EditRebuild3(),"Double-D rebuild failed");Check(FeatureType(cut)=="Cut","Double-D must be a native extruded cut");Body(d);Prop(d,"Coupling Opening Template",block.SourceFile);return cut;
  }

  void Line(IModelDoc2 d,IMathTransform tr,double[] a,double[] b){a=SketchPoint(a,tr);b=SketchPoint(b,tr);Check(d.SketchManager.CreateLine(a[0],a[1],0,b[0],b[1],0)!=null,"Sketch line failed");}
  void Arc(IModelDoc2 d,IMathTransform tr,double[] a,double[] b,double[] middle){a=SketchPoint(a,tr);b=SketchPoint(b,tr);middle=SketchPoint(middle,tr);Check(d.SketchManager.Create3PointArc(a[0],a[1],0,b[0],b[1],0,middle[0],middle[1],0)!=null,"Sketch arc failed");}
  void GrooveHalf(IModelDoc2 d,GrooveSpec spec,double ro,double ri,bool inner) {
   string prefix=inner?"RollGrooveInner":"RollGrooveOuter";Free(d,prefix+"Profile");Activate(d);d.ClearSelection2(true);Check(d.Extension.SelectByID2("Front Plane","PLANE",0,0,0,false,0,null,0),"Verified axial plane missing");d.SketchManager.InsertSketch(true);ISketch sk=d.SketchManager.ActiveSketch as ISketch;IMathTransform tr=sk.ModelToSketchTransform;
   double a=spec.A,b=a+spec.B,r=inner?ri:ro,depth=ro-spec.C/2,g=r-depth,q=spec.BlendRadius,t=q/Math.Sqrt(2),up=inner?.0001:.001;
   double[][] p={V(a,r,0),V(a+q,r-q,0),V(a+q,g+q,0),V(a+2*q,g,0),V(b-2*q,g,0),V(b-q,g+q,0),V(b-q,r-q,0),V(b,r,0),V(b,r+up,0),V(a,r+up,0)};
   d.SketchManager.AddToDB=true;
   try{Arc(d,tr,p[0],p[1],V(a+t,r-q+t,0));Line(d,tr,p[1],p[2]);Arc(d,tr,p[2],p[3],V(a+2*q-t,g+q-t,0));Line(d,tr,p[3],p[4]);Arc(d,tr,p[4],p[5],V(b-2*q+t,g+q-t,0));Line(d,tr,p[5],p[6]);Arc(d,tr,p[6],p[7],V(b-t,r-q+t,0));Line(d,tr,p[7],p[8]);Line(d,tr,p[8],p[9]);Line(d,tr,p[9],p[0]);}finally{d.SketchManager.AddToDB=false;}
   double[] c=SketchPoint(V(0,0,0),tr),c2=SketchPoint(V(.1,0,0),tr);ISketchSegment axis=d.SketchManager.CreateCenterLine(c[0],c[1],0,c2[0],c2[1],0) as ISketchSegment;d.SketchManager.InsertSketch(true);IFeature profile=sk as IFeature;Name(profile,prefix+"Profile");d.ClearSelection2(true);profile.Select2(false,0);axis.Select2(true,4);
   IFeature f=d.FeatureManager.FeatureRevolve2(true,true,false,!inner,false,false,0,0,2*Math.PI,0,false,false,0,0,0,0,0,true,true,true) as IFeature;Name(f,inner?"RolledWallInnerDisplacement":"Cut-Revolve1");Check(d.EditRebuild3(),"Roll groove rebuild failed");Body(d);
  }
  public void AddRollGroove(IModelDoc2 d,GrooveSpec spec,double actualOD,double actualID,double stockLength) {
   Own(d);Free(d,"Cut-Revolve1","RolledWallInnerDisplacement","RollGrooveOuterProfile","RollGrooveInnerProfile");
   Check(spec.ReviewedForJob&&spec.System=="OGS"&&spec.Method=="roll"&&!string.IsNullOrEmpty(spec.Source)&&!string.IsNullOrEmpty(spec.Revision),"Applicable groove specification must be reviewed for this job; no cut-groove fallback");
   double depth=(actualOD-spec.C)/2;Check(Math.Abs(spec.OD-actualOD)<1e-7&&actualOD>actualID&&spec.A>0&&spec.B>4*spec.BlendRadius&&depth>2*spec.BlendRadius&&spec.BlendRadius>0&&actualID/2>depth,"Groove dimensions/OD mismatch");
   Check((actualOD-actualID)/2>=spec.MinimumWall-1e-7&&spec.A+spec.B<stockLength,"Wall/stock length outside reviewed specification");
   GrooveHalf(d,spec,actualOD/2,actualID/2,false);GrooveHalf(d,spec,actualOD/2,actualID/2,true);
   PartSnapshot s=Snapshot(d);bool c=false,inside=false;foreach(double radius in s.CylinderRadii){if(Math.Abs(radius-spec.C/2)<1e-7)c=true;if(Math.Abs(radius-(actualID/2-depth))<1e-7)inside=true;}Check(c&&inside,"Groove diameter or rolled inner displacement differs from specification");
   Prop(d,"Groove Specification",spec.Source+"; "+spec.Revision+"; OGS roll; A="+spec.A+" B="+spec.B+" C="+spec.C+" m");
  }
 }
}


