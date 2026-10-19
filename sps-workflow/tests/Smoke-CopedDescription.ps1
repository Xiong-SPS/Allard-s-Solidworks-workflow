$index = Get-Content (Join-Path $SpsWorkflowRoot 'index\library-index.json') -Raw | ConvertFrom-Json
$pipe = $index.entries | Where-Object { $_.part_number -eq 'PIPE 3 S10' }
$result = [SpsWorkflow.CopedDescriptionSelfTest]::Run($SpsApplication,$SpsWorkspace,$SpsWorkflowRoot,$pipe.path,$pipe.sha256)
$result | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $result.OutputFolder 'coped-description-test-report.json') -Encoding UTF8
Write-Output ('Coped description report: ' + (Join-Path $result.OutputFolder 'coped-description-test-report.json'))
if ($result.Status -ne 'passed') { throw $result.Error }
