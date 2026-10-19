namespace SpsWorkflow {
 using System;
 using System.IO;
 using System.Collections.Generic;
 using SolidWorks.Interop.sldworks;
 public sealed class PartTask {
  public string Id;
  public string[] Dependencies=new string[0];
  public Func<RecipeResult> Build;
 }
 public sealed class TaskResult {
  public string Id,Status="pending",Error;
  public RecipeResult Result;
 }
 public sealed class BatchReport {
  public string Status="running",PlanFile,PlanSha256,StartedUtc,FinishedUtc;
  public List<TaskResult> Parts=new List<TaskResult>();
  public List<string> Warnings=new List<string>();
  public int Checkpoint;
 }
 public static class PartBatch {
  // One typed call processes the batch. Every completed part has an on-disk
  // receipt before the next part starts; independent work survives a failure.
  public static BatchReport Run(object application,string project,string outputFolder,string reviewedPlan,string planHash,PartTask[] tasks) {
   ISldWorks app=(ISldWorks)application;string folder=Path.GetFullPath(outputFolder),workspace=Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar);
   if(!folder.StartsWith(workspace+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(folder))throw new ArgumentException("Existing project output folder required");
   if(tasks==null||tasks.Length==0||SpsSession.Hash(reviewedPlan)!=planHash)throw new ArgumentException("Tasks and unchanged reviewed plan required");
   var map=new Dictionary<string,TaskResult>();foreach(PartTask task in tasks){if(task==null||string.IsNullOrWhiteSpace(task.Id)||task.Build==null||task.Dependencies==null||map.ContainsKey(task.Id))throw new ArgumentException("Unique task IDs, dependency arrays and builders required");map[task.Id]=new TaskResult{Id=task.Id};}
   foreach(PartTask task in tasks)foreach(string id in task.Dependencies)if(!map.ContainsKey(id)||id==task.Id)throw new ArgumentException("Unknown/self dependency: "+id);
   IModelDoc2 before=app.ActiveDoc as IModelDoc2;bool dirty=before!=null&&before.GetSaveFlag();
   var report=new BatchReport{PlanFile=Path.GetFullPath(reviewedPlan),PlanSha256=planHash,StartedUtc=DateTime.UtcNow.ToString("o")};foreach(PartTask task in tasks)report.Parts.Add(map[task.Id]);
   string prefix=Path.Combine(folder,"batch-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
   Action checkpoint=delegate(){report.Checkpoint++;EvidenceJson.WriteNew(prefix+"-"+report.Checkpoint.ToString("D4")+".json",report);};checkpoint();
   try {
    int remaining=tasks.Length;while(remaining>0){bool progressed=false;foreach(PartTask task in tasks){TaskResult state=map[task.Id];if(state.Status!="pending")continue;bool waiting=false,blocked=false;foreach(string id in task.Dependencies){if(map[id].Status=="pending")waiting=true;else if(map[id].Status!="passed")blocked=true;}
     if(blocked){state.Status="blocked";state.Error="Dependency failed or blocked";}else if(waiting)continue;else{try{if(SpsSession.Hash(reviewedPlan)!=planHash)throw new InvalidOperationException("Plan changed during batch");state.Result=task.Build();if(state.Result==null||!state.Result.Saved||!state.Result.Closed||state.Result.Verification==null||state.Result.Verification.Status!="passed"||state.Result.Verification.Expected.PlanSha256!=planHash||state.Result.Verification.Expected.PartId!=task.Id)throw new InvalidOperationException("Builder did not return saved/closed evidence bound to this plan and part ID");state.Status="passed";}catch(Exception error){state.Status="failed";state.Error=error.ToString();}}
     progressed=true;remaining--;checkpoint();}
     if(!progressed){foreach(TaskResult state in report.Parts)if(state.Status=="pending"){state.Status="blocked";state.Error="Cyclic dependencies";remaining--;}checkpoint();}
    }
    report.Status="passed";foreach(TaskResult state in report.Parts)if(state.Status!="passed")report.Status="incomplete";
   }finally{if(before!=null){if(before.GetSaveFlag()!=dirty)report.Warnings.Add("Original active document save state changed; inspect it");int error=0;app.ActivateDoc3(before.GetTitle(),false,0,ref error);}report.FinishedUtc=DateTime.UtcNow.ToString("o");checkpoint();}
   return report;
  }
 }
}
