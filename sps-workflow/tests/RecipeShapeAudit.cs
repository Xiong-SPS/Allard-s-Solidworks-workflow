namespace SpsWorkflow {
 using System;
 using SolidWorks.Interop.sldworks;
 public static class RecipeShapeAudit {
  public static double ElbowVolume(object application,string file,double angle,double bendRadius,double od,double id) {
   ISldWorks app=(ISldWorks)application;IModelDoc2 before=app.ActiveDoc as IModelDoc2;string hash=SpsSession.Hash(file);int e=0,w=0;
   IModelDoc2 d=app.GetOpenDocumentByName(file) as IModelDoc2;bool owns=d==null;if(owns)d=app.OpenDoc6(file,1,1,"",ref e,ref w) as IModelDoc2;
   try {
    if(d==null||e!=0)throw new InvalidOperationException("Could not reload recipe elbow");
    object[] bodies=((IPartDoc)d).GetBodies2(0,false) as object[];if(bodies==null||bodies.Length!=1)throw new InvalidOperationException("Elbow requires one solid");
    IBody2 body=(IBody2)bodies[0];if(body.Check2()!=0)throw new InvalidOperationException("Invalid elbow solid");
    double volume=((double[])body.GetMassProperties(1))[3];double expected=angle*bendRadius*Math.PI*(od*od-id*id)/4;
    if(Math.Abs(volume-expected)>expected*1e-5)throw new InvalidOperationException("Actual elbow sector volume disagrees with requested angle/material: "+volume+" versus "+expected);
    return volume;
   }finally{if(owns&&d!=null)app.CloseDoc(d.GetTitle());if(SpsSession.Hash(file)!=hash)throw new InvalidOperationException("Read-back audit changed fixture");if(before!=null){int error=0;app.ActivateDoc3(before.GetTitle(),false,0,ref error);}}
  }
 }
}
