namespace SpsWorkflow {
 using System;
 using System.Globalization;
 using System.Text.RegularExpressions;
 using SolidWorks.Interop.sldworks;
 using SolidWorks.Interop.swconst;
 public static class PropertyLengthFormat {
  public const int MaximumDecimals=3;
  public static readonly string[] Names={"Stock Length","Cut Length","Max Modeled Length","Finished Length","Overall Length","Length"};
  const string Number=@"[-+]?(?:\d+\.\d+|\d+)";
  public static string Format(double value){if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentException("Finite length required");return Math.Round(value,MaximumDecimals,MidpointRounding.AwayFromZero).ToString("0.###",CultureInfo.InvariantCulture);}
  static string ReplaceNumber(Match match){Group group=match.Groups["number"];int start=group.Index-match.Index;return match.Value.Substring(0,start)+Format(double.Parse(group.Value,CultureInfo.InvariantCulture))+match.Value.Substring(start+group.Length);}
  public static string Literal(string value){if(string.IsNullOrEmpty(value))return value;return Regex.Replace(value,@"^\s*(?<number>"+Number+@")\s*(?:in(?:ch(?:es)?)?|mm|m|\""|\s)*$",ReplaceNumber,RegexOptions.IgnoreCase);}
  public static string Description(string value){if(string.IsNullOrEmpty(value))return value;
   value=Regex.Replace(value,@"\b(?:stock|cut|finished|overall|max(?:imum)?(?:\s+modeled)?)\s+length\s*[:=]?\s*(?<number>"+Number+@")\s*(?:in(?:ch(?:es)?)?|mm|\""|\b)",ReplaceNumber,RegexOptions.IgnoreCase);
   return Regex.Replace(value,@"(?<![\w.])(?<number>"+Number+@")\s*(?:in(?:ch(?:es)?)?|mm|\"")\s*(?:(?:stock|cut|finished)\s+)?(?:length|long)\b",ReplaceNumber,RegexOptions.IgnoreCase);
  }
  public static bool ExcessDecimals(string value){return value!=null&&Regex.IsMatch(value,@"(?<!\w)[-+]?\d+\.\d{4,}(?!\d)");}
  public static string CopedDescription(string basis,string length,string unit) {
   basis=basis??"";string linked=Regex.Replace(basis,"\"(?:D1@Boss-Extrude1|RD1@Annotations)\"",delegate(Match match){return length;});
   if(linked!=basis)return linked;
   // Explicit stock/finished length phrases are replaced; nominal pipe size,
   // schedule, material and feature notes remain untouched.
   string text=Regex.Replace(basis,@"\b(?:max(?:imum)?\s+modeled|stock|cut|finished|overall)\s+length\s*[:=]?\s*"+Number+@"\s*(?:in(?:ch(?:es)?)?|mm|cm|m|ft|\"")?", "",RegexOptions.IgnoreCase);
   text=Regex.Replace(text,@"(?<![\w.])"+Number+@"\s*(?:in(?:ch(?:es)?)?|mm|cm|m|ft|\"")\s*(?:(?:stock|cut|finished)\s+)?(?:length|long)\b", "",RegexOptions.IgnoreCase).Trim(' ', ',', ';');
   return (text.Length==0?"PIPE":text)+", MAX MODELED LENGTH "+length+" "+unit;
  }
 }
 public sealed partial class SpsSession {
  void SetCopedDescriptionValues(IModelDoc2 d,System.Collections.Generic.Dictionary<string,string> values) {
   IBody2 body=Body(d);double min,y,z,max;body.GetExtremePoint(1,0,0,out max,out y,out z);body.GetExtremePoint(-1,0,0,out min,out y,out z);double length=max-min;Check(length>0,"Invalid finished pipe extent");
   int unit=d.Extension.GetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitsLinear,(int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);double divisor;string suffix;
   switch((swLengthUnit_e)unit){case swLengthUnit_e.swINCHES:divisor=.0254;suffix="in";break;case swLengthUnit_e.swMM:divisor=.001;suffix="mm";break;case swLengthUnit_e.swCM:divisor=.01;suffix="cm";break;case swLengthUnit_e.swMETER:divisor=1;suffix="m";break;case swLengthUnit_e.swFEET:divisor=.3048;suffix="ft";break;default:throw new InvalidOperationException("Coped length description requires a supported decimal length unit");}
   string formatted=PropertyLengthFormat.Format(length/divisor);values["Max Modeled Length"]=formatted+" "+suffix;values["Part Description"]=PropertyLengthFormat.CopedDescription(Property(d,"Pipe Description Template"),formatted,suffix);values["Stock Length"]="\"D1@Boss-Extrude1\" "+suffix;
  }
  public void RefreshCopedPipeDescription(IModelDoc2 d) {
   Own(d);Check(Property(d,"Length Description Basis")=="Max Modeled Length","Set coped-pipe metadata before final verification");var values=new System.Collections.Generic.Dictionary<string,string>();SetCopedDescriptionValues(d,values);ICustomPropertyManager cfg=d.Extension.CustomPropertyManager[d.ConfigurationManager.ActiveConfiguration.Name];
   foreach(var pair in values){Prop(d,pair.Key,pair.Value);if(cfg.GetType2(pair.Key)==30)cfg.Add3(pair.Key,30,pair.Value,2);}
  }
  public void SetLengthPropertyPrecision(IModelDoc2 d) {
   Own(d);IModelDocExtension extension=d.Extension;int option=(int)swUserPreferenceOption_e.swDetailingNoOptionSpecified;
   // A custom unit system permits a decimal precision override. Preserve all
   // existing unit choices; this changes display formatting, never geometry.
   var units=new System.Collections.Generic.Dictionary<int,int>();foreach(swUserPreferenceIntegerValue_e pref in new swUserPreferenceIntegerValue_e[]{swUserPreferenceIntegerValue_e.swUnitsLinear,swUserPreferenceIntegerValue_e.swUnitsAngular,swUserPreferenceIntegerValue_e.swUnitsMassPropLength,swUserPreferenceIntegerValue_e.swUnitsMassPropMass,swUserPreferenceIntegerValue_e.swUnitsMassPropVolume})units[(int)pref]=extension.GetUserPreferenceInteger((int)pref,option);
   Check(extension.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitSystem,option,(int)swUnitSystem_e.swUnitSystem_Custom),"Could not enable custom length display precision");
   foreach(var unit in units)if(extension.GetUserPreferenceInteger(unit.Key,option)!=unit.Value)Check(extension.SetUserPreferenceInteger(unit.Key,option,unit.Value),"Could not preserve document unit choice");
   Check(extension.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitsLinearDecimalDisplay,option,(int)swFractionDisplay_e.swDECIMAL),"Could not set decimal length display");
   Check(extension.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitsLinearDecimalPlaces,option,PropertyLengthFormat.MaximumDecimals),"Could not set three-decimal length property precision");
  }
 }
}
