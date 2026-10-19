$index = Get-Content (Join-Path $SpsWorkflowRoot 'index\library-index.json') -Raw | ConvertFrom-Json
$pipe = $index.entries | Where-Object { $_.part_number -eq 'PIPE 3 S10' }
$elbow = $index.entries | Where-Object { $_.part_number -eq 'I414A6' }
$flange = $index.entries | Where-Object { $_.part_number -eq 'F4SQ534X3' }
$block = 'H:\Engineering documents\Solidworks\Blocks\34 HALF-COUPLING.SLDBLK'
$blockHash = [SpsWorkflow.SpsSession]::Hash($block)
$evidence = Join-Path $SpsWorkflowRoot 'component-plan-guide.md'
$protectedFiles = [string[]](@($index.entries.path) + @($block))
$result = [SpsWorkflow.RecipeSelfTest]::Run($SpsApplication,$SpsWorkspace,$SpsWorkflowRoot,$pipe.path,$pipe.sha256,$elbow.path,$elbow.sha256,$flange.path,$flange.sha256,$block,$blockHash,$evidence,$protectedFiles)
$result | ConvertTo-Json -Depth 15 | Set-Content (Join-Path $result.OutputFolder 'recipe-test-report.json') -Encoding UTF8
Write-Output ('Recipe report: ' + (Join-Path $result.OutputFolder 'recipe-test-report.json'))
if($result.Status -ne 'passed'){throw $result.Error}
