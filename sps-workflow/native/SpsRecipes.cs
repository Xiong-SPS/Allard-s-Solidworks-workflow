namespace SpsWorkflow {
 using System;
 using System.IO;
 using System.Collections.Generic;
 using SolidWorks.Interop.sldworks;

 public sealed class RecipeSource {
  public string Template, TemplateSha256, Configuration, Filename, CustomerSourceFile, StockMappingEvidence;
  public string PlanFile,PlanSha256,PartId;
  public Dictionary<string,string> Properties=new Dictionary<string,string>();
 }
 public sealed class CopeRecipe {
  public double[][] GuidePoints;
  public string FeatureName="MidWallLoftedCope";
 }
 public sealed class OpeningRecipe {
  public string Name;
  public double[] OutsideStart, InteriorStop;
  public double BranchOD, BranchID;
  public IBoreRegion ParentBore;
 }
 public sealed class DoubleDRecipe {
  public string Name;
  public DoubleDProfile Block;
  public double[] WeldCenter, Outward, LongAxis, InteriorStop;
  public IBoreRegion ParentBore;
 }
 public sealed class PipeRecipe {
  public RecipeSource Source;
  public double StockLength, OD, ID;
  public CopeRecipe Cope;
  // Groove is on the blank's X=0 end; cope is on its X=stock-length end.
  public GrooveSpec Groove;
  public List<OpeningRecipe> Openings=new List<OpeningRecipe>();
  public List<DoubleDRecipe> DoubleDOpenings=new List<DoubleDRecipe>();
 }
 public sealed class ElbowRecipe {
  public RecipeSource Source;
  public double OriginalAngle, TargetAngle, BendRadius, OD, ID;
  public List<OpeningRecipe> Openings=new List<OpeningRecipe>();
  public List<DoubleDRecipe> DoubleDOpenings=new List<DoubleDRecipe>();
 }
 public sealed class FittingRecipe {
  public RecipeSource Source;
  public DimensionEdit[] Edits;
  public Dictionary<string,double> PreservedDimensions;
  public string DimensionEvidence;
 }
 public sealed class RecipeResult {
  public string File;
  public string[] UnknownProperties;
  public PartSnapshot Snapshot;
  public PartVerification Verification;
  public bool Saved, Closed;
 }

 // Compose the verified low-level operations in a consistent order. Failures
 // retain the new owned copy for diagnosis; never edit or close unrelated docs.
 public static class PartRecipes {
  static void Require(bool condition,string message){if(!condition)throw new ArgumentException(message);}
  static bool Positive(double x){return x>0&&!double.IsNaN(x)&&!double.IsInfinity(x);}
  static void Source(RecipeSource source,bool pipe) {
   Require(source!=null&&!string.IsNullOrEmpty(source.Template)&&!string.IsNullOrEmpty(source.TemplateSha256)&&!string.IsNullOrEmpty(source.Configuration)&&!string.IsNullOrEmpty(source.Filename)&&source.Properties!=null,"Complete verified library source and metadata dictionary required");
   Require(string.IsNullOrEmpty(source.CustomerSourceFile)||File.Exists(source.CustomerSourceFile),"Customer source file missing");
   if(!string.IsNullOrEmpty(source.PlanFile)||!string.IsNullOrEmpty(source.PlanSha256)||!string.IsNullOrEmpty(source.PartId))Require(!string.IsNullOrEmpty(source.PlanFile)&&!string.IsNullOrEmpty(source.PlanSha256)&&!string.IsNullOrEmpty(source.PartId)&&SpsSession.Hash(source.PlanFile)==source.PlanSha256,"Part recipe requires unchanged plan and stable part ID");
   string stock;if(!pipe&&source.Properties.TryGetValue("Material ID",out stock)&&!string.IsNullOrWhiteSpace(stock))Require(!string.IsNullOrEmpty(source.StockMappingEvidence)&&File.Exists(source.StockMappingEvidence),"Modified fitting ERP mapping requires evidence before copying");
  }
  static void Openings(SpsSession session,IModelDoc2 d,List<OpeningRecipe> circles,List<DoubleDRecipe> slots) {
   foreach(OpeningRecipe o in circles)session.AddBranchOpening(d,o.OutsideStart,o.InteriorStop,o.BranchOD,o.BranchID,o.ParentBore,o.Name);
   foreach(DoubleDRecipe o in slots)session.AddDoubleD(d,o.Block,o.WeldCenter,o.Outward,o.LongAxis,o.InteriorStop,o.ParentBore,o.Name);
  }
  static RecipeResult Finish(SpsSession session,IModelDoc2 d,RecipeSource source,bool pipe,PartExpectations expected) {
   Require(d.EditRebuild3(),"Recipe final rebuild failed");
   expected.PlanFile=source.PlanFile;expected.PlanSha256=source.PlanSha256;expected.PartId=source.PartId;
   RecipeResult result=new RecipeResult{File=d.GetPathName(),UnknownProperties=session.ApplyProperties(d,source.Properties,pipe,!pipe,source.CustomerSourceFile,source.StockMappingEvidence,expected.CopedPipe)};
   result.Verification=session.CompletePart(d,expected);result.Snapshot=result.Verification.Snapshot;result.Saved=result.Verification.Saved;result.Closed=result.Verification.Closed;return result;
  }
  static void ExpectOpenings(PartExpectations expected,List<OpeningRecipe> circles,List<DoubleDRecipe> slots){foreach(OpeningRecipe o in circles)expected.Features.Add(new FeatureExpectation{Name=o.Name,Type="BlendCut",Radius=(o.BranchOD+o.BranchID)/4,Start=o.OutsideStart,Stop=o.InteriorStop});foreach(DoubleDRecipe o in slots)expected.Features.Add(new FeatureExpectation{Name=o.Name,Type="Cut",ProfileEdges=8,DoubleD=true});}
  public static RecipeResult Pipe(SpsSession session,PipeRecipe recipe) {
   Require(recipe!=null,"Pipe recipe required");Source(recipe.Source,true);
   Require(Positive(recipe.StockLength)&&Positive(recipe.ID)&&Positive(recipe.OD)&&recipe.OD>recipe.ID,"Finite actual pipe dimensions required");
   Require(recipe.Openings!=null&&recipe.DoubleDOpenings!=null,"Opening collections required");
   if(recipe.Groove!=null)Require(recipe.Groove.ReviewedForJob&&recipe.Groove.Method=="roll"&&recipe.Groove.System=="OGS","Reviewed OGS roll specification required; cut grooves are unsupported");
   if(recipe.Cope!=null)Require(recipe.Cope.GuidePoints!=null&&!string.IsNullOrWhiteSpace(recipe.Cope.FeatureName),"Reviewed native mid-wall cope guide required");
   if(recipe.Cope!=null&&recipe.Groove!=null)foreach(double[] p in recipe.Cope.GuidePoints)Require(p!=null&&p.Length==3&&p[0]>recipe.Groove.A+recipe.Groove.B,"Cope may not overlap the grooved end's straight section");
   IModelDoc2 d=session.Copy(recipe.Source.Template,recipe.Source.Filename,recipe.Source.Configuration,recipe.Source.TemplateSha256);
   PartSnapshot stock=session.SetPipeLength(d,recipe.StockLength,recipe.OD,recipe.ID);var expected=new PartExpectations{Configuration=recipe.Source.Configuration,Material=stock.Material,CopedPipe=recipe.Cope!=null};expected.Dimensions["D1@Boss-Extrude1"]=recipe.StockLength;expected.CylinderRadii.Add(recipe.OD/2);expected.CylinderRadii.Add(recipe.ID/2);
   if(recipe.Cope!=null)session.AddMidWallCope(d,recipe.StockLength,(recipe.OD+recipe.ID)/4,(recipe.OD-recipe.ID)/2,recipe.Cope.GuidePoints,recipe.Cope.FeatureName);
   if(recipe.Groove!=null)session.AddRollGroove(d,recipe.Groove,recipe.OD,recipe.ID,recipe.StockLength);
   if(recipe.Cope!=null)expected.Features.Add(new FeatureExpectation{Name=recipe.Cope.FeatureName,Type="BlendCut",ReferencedSketch=recipe.Cope.FeatureName+"Guide"});
   if(recipe.Groove!=null){expected.Features.Add(new FeatureExpectation{Name="Cut-Revolve1",Type="RevCut",ReferencedSketch="RollGrooveOuterProfile"});expected.Features.Add(new FeatureExpectation{Name="RolledWallInnerDisplacement",Type="Revolution",ReferencedSketch="RollGrooveInnerProfile"});expected.CylinderRadii.Add(recipe.Groove.C/2);expected.CylinderRadii.Add(recipe.ID/2-(recipe.OD-recipe.Groove.C)/2);}
   Openings(session,d,recipe.Openings,recipe.DoubleDOpenings);ExpectOpenings(expected,recipe.Openings,recipe.DoubleDOpenings);return Finish(session,d,recipe.Source,true,expected);
  }
  public static RecipeResult Elbow(SpsSession session,ElbowRecipe recipe) {
   Require(recipe!=null,"Elbow recipe required");Source(recipe.Source,false);Require(recipe.Openings!=null&&recipe.DoubleDOpenings!=null,"Opening collections required");
   Require(Positive(recipe.OriginalAngle)&&Positive(recipe.TargetAngle)&&recipe.TargetAngle<=recipe.OriginalAngle&&recipe.OriginalAngle<=Math.PI,"Reviewed trimmed elbow angles required");
   IModelDoc2 d=session.Copy(recipe.Source.Template,recipe.Source.Filename,recipe.Source.Configuration,recipe.Source.TemplateSha256);
   PartSnapshot stock=session.SetElbowAngle(d,recipe.OriginalAngle,recipe.TargetAngle,recipe.BendRadius,recipe.OD,recipe.ID);var expected=new PartExpectations{Configuration=recipe.Source.Configuration,Material=stock.Material};expected.Dimensions["D1@Revolve1"]=recipe.TargetAngle;expected.Dimensions["D1@Sketch1"]=recipe.OD;expected.Dimensions["D2@Sketch1"]=recipe.BendRadius;expected.Dimensions["D3@Sketch1"]=(recipe.OD-recipe.ID)/2;
   Openings(session,d,recipe.Openings,recipe.DoubleDOpenings);ExpectOpenings(expected,recipe.Openings,recipe.DoubleDOpenings);return Finish(session,d,recipe.Source,false,expected);
  }
  public static RecipeResult Fitting(SpsSession session,FittingRecipe recipe) {
   Require(recipe!=null,"Fitting recipe required");Source(recipe.Source,false);
   Require(!string.IsNullOrWhiteSpace(recipe.DimensionEvidence)&&File.Exists(recipe.DimensionEvidence),"Record actual dimension/search evidence before editing a library fitting");
   Require(recipe.Edits!=null&&recipe.Edits.Length>0&&recipe.PreservedDimensions!=null&&recipe.PreservedDimensions.Count>0,"Native dimension edits and protected dimensions required");
   IModelDoc2 d=session.Copy(recipe.Source.Template,recipe.Source.Filename,recipe.Source.Configuration,recipe.Source.TemplateSha256);
   PartSnapshot stock=session.EditExistingDimensions(d,recipe.Edits,recipe.PreservedDimensions);var expected=new PartExpectations{Configuration=recipe.Source.Configuration,Material=stock.Material};foreach(var pair in recipe.PreservedDimensions)expected.Dimensions[pair.Key]=pair.Value;foreach(DimensionEdit edit in recipe.Edits)expected.Dimensions[edit.Name]=edit.Target;return Finish(session,d,recipe.Source,false,expected);
  }
 }
}
