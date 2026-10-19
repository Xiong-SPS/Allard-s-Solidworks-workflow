$index = Get-Content (Join-Path $SpsWorkflowRoot 'index\library-index.json') -Raw | ConvertFrom-Json
$pipe = $index.entries | Where-Object { $_.part_number -eq 'PIPE 3 S10' }
$parent = $index.entries | Where-Object { $_.part_number -eq 'PIPE 6 S10' }
$regression = Join-Path $SpsWorkspace '16186-262-06\16186-262-06-04.SLDPRT'
$block = 'H:\Engineering documents\Solidworks\Blocks\34 HALF-COUPLING.SLDBLK'
$protectedFiles = [string[]](@($index.entries.path) + @($regression,$block))
$result = [SpsWorkflow.NativeSelfTest]::Run($SpsApplication,$SpsWorkspace,$SpsWorkflowRoot,$pipe.path,$pipe.sha256,$pipe.geometry.od_m,$pipe.geometry.id_m,$parent.geometry.od_m,$regression,$block,'2026-10-15',$protectedFiles)
$result | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $result.OutputFolder 'self-test-report.json') -Encoding UTF8
Write-Output ('Self-test report: ' + (Join-Path $result.OutputFolder 'self-test-report.json'))
if($result.Status -ne 'passed'){throw $result.Error}
