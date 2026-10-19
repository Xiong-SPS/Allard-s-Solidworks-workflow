$ErrorActionPreference = 'Stop'
$runtime = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
if (-not (Test-Path -LiteralPath $runtime)) { $runtime = (Get-Command python -ErrorAction Stop).Source }
& $runtime (Join-Path $PSScriptRoot 'component_plan.py') @args
exit $LASTEXITCODE
