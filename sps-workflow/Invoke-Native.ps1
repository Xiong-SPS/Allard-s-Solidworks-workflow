param(
    [string]$ActionScript,
    [string]$AdditionalSource,
    [int]$SolidWorksPid = 0,
    [switch]$Offline,
    [switch]$CompileOnly,
    [string]$SolidWorksInteropDirectory = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS (2)'
)
$ErrorActionPreference = 'Stop'
# The installed SolidWorks interop is tested here with Windows PowerShell/.NET Framework.
if ($PSVersionTable.PSVersion.Major -gt 5) {
    # Native executables do not accept PowerShell hashtable splatting.
    $forwardedArguments = @()
    foreach ($parameterName in $PSBoundParameters.Keys) {
        $parameterValue = $PSBoundParameters[$parameterName]
        if ($parameterValue -is [System.Management.Automation.SwitchParameter]) {
            if ($parameterValue.IsPresent) { $forwardedArguments += "-$parameterName" }
        } else {
            $forwardedArguments += "-$parameterName"
            $forwardedArguments += [string]$parameterValue
        }
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath @forwardedArguments
    exit $LASTEXITCODE
}
$SpsWorkflowRoot = $PSScriptRoot
$SpsWorkspace = Split-Path $PSScriptRoot -Parent
$interop = Join-Path $SolidWorksInteropDirectory 'SolidWorks.Interop.sldworks.dll'
$constants = Join-Path $SolidWorksInteropDirectory 'SolidWorks.Interop.swconst.dll'
foreach ($assemblyPath in @($interop, $constants)) {
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "SolidWorks interop assembly not found: $assemblyPath. Supply -SolidWorksInteropDirectory for this PC's installation."
    }
}
Add-Type -Path $interop
Add-Type -Path $constants
Add-Type -AssemblyName System.Web.Extensions
$source = Get-Content (Join-Path $PSScriptRoot 'native\SpsSolidWorks.cs') -Raw
$source += "`n" + (Get-Content (Join-Path $PSScriptRoot 'native\SpsRecipes.cs') -Raw)
$source += "`n" + (Get-Content (Join-Path $PSScriptRoot 'native\SpsVerification.cs') -Raw)
$source += "`n" + (Get-Content (Join-Path $PSScriptRoot 'native\SpsBatch.cs') -Raw)
$source += "`n" + (Get-Content (Join-Path $PSScriptRoot 'native\SpsAssemblyVerification.cs') -Raw)
$source += "`n" + (Get-Content (Join-Path $PSScriptRoot 'native\SpsLengthProperties.cs') -Raw)
if ($AdditionalSource) { $source += "`n" + (Get-Content -LiteralPath $AdditionalSource -Raw) }
Add-Type -ReferencedAssemblies @($interop,$constants,[System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location) -TypeDefinition $source
if ($CompileOnly) { Write-Output 'PASS native module compiled'; return }
if (-not $ActionScript) { throw 'Provide -ActionScript or -CompileOnly.' }
if ($Offline) { . ([IO.Path]::GetFullPath($ActionScript)); return }
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
public static class SpsRunningApplication {
 [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved,out IRunningObjectTable table);
 [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved,out IBindCtx context);
 public static object Attach(int requestedPid) {
  IRunningObjectTable table;GetRunningObjectTable(0,out table);IBindCtx context;CreateBindCtx(0,out context);
  IEnumMoniker enumerator;table.EnumRunning(out enumerator);IMoniker[] item=new IMoniker[1];var candidates=new List<IMoniker>();
  while(enumerator.Next(1,item,IntPtr.Zero)==0){string name;item[0].GetDisplayName(context,null,out name);Match m=Regex.Match(name,@"^!?SolidWorks_PID_(\d+)$",RegexOptions.IgnoreCase);if(m.Success&&(requestedPid==0||int.Parse(m.Groups[1].Value)==requestedPid))candidates.Add(item[0]);}
  if(candidates.Count!=1)throw new InvalidOperationException("Expected exactly one SolidWorks application moniker; supply -SolidWorksPid if multiple sessions exist. Found "+candidates.Count);
  object result;table.GetObject(candidates[0],out result);return result;
 }
}
'@
$SpsApplication = [SpsRunningApplication]::Attach($SolidWorksPid)
. ([IO.Path]::GetFullPath($ActionScript))
