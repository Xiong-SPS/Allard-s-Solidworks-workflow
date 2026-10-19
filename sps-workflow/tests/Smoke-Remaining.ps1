$index = Get-Content (Join-Path $SpsWorkflowRoot 'index\library-index.json') -Raw | ConvertFrom-Json
$pipe = $index.entries | Where-Object { $_.part_number -eq 'PIPE 3 S10' }
$block = 'H:\Engineering documents\Solidworks\Blocks\34 HALF-COUPLING.SLDBLK'
$result = [SpsWorkflow.RemainingSelfTest]::Run($SpsApplication,$SpsWorkspace,$SpsWorkflowRoot,$pipe.path,$pipe.sha256,$block,[SpsWorkflow.SpsSession]::Hash($block))
$result | ConvertTo-Json -Depth 25 | Set-Content (Join-Path $result.OutputFolder 'remaining-test-report.json') -Encoding UTF8
Write-Output ('Remaining workflow report: ' + (Join-Path $result.OutputFolder 'remaining-test-report.json'))
if ($result.Status -ne 'passed') { throw $result.Error }
