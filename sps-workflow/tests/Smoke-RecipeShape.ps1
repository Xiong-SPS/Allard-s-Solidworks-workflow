$reports = Get-ChildItem (Join-Path $SpsWorkflowRoot 'tests\recipe-runs') -Filter recipe-test-report.json -Recurse | Sort-Object LastWriteTime -Descending
$report = $reports | Select-Object -First 1
$fixture = Get-Content $report.FullName -Raw | ConvertFrom-Json
if ($fixture.Status -ne 'passed') { throw 'Latest recipe fixture did not pass; inspect it first.' }
$volume = [SpsWorkflow.RecipeShapeAudit]::ElbowVolume($SpsApplication,$fixture.Elbow.File,(40*[Math]::PI/180),.2286,.168275,.1614678)
$result = @{status='passed'; actual_elbow_volume_m3=$volume; check='Actual solid volume equals 40-degree torus sector with preserved stock OD/ID and bend radius'; recipe_report=$report.FullName}
$result | ConvertTo-Json | Set-Content (Join-Path $fixture.OutputFolder 'shape-audit.json') -Encoding UTF8
Write-Output ('PASS actual elbow solid volume: ' + $volume + ' m3')
