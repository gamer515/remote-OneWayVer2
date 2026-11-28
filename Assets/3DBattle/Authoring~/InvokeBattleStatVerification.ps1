param([string]$Entry = 'VerifyDecisionBattleStats.Inspect', [string]$Script = 'Assets/3DBattle/Authoring~/VerifyDecisionBattleStats.cs')
$ErrorActionPreference = 'Stop'
$connection = Get-Content -LiteralPath 'Library/Pipeline/.unity-pipeline-port' -Raw | ConvertFrom-Json
$body = @{command='run_script'; parameters=@{file=$Script; entry=$Entry; timeout_ms=30000}} | ConvertTo-Json -Depth 8
Invoke-RestMethod -Uri "http://127.0.0.1:$($connection.port)/api/exec" -Method Post -Headers @{Authorization="Bearer $($connection.evalToken)"} -ContentType 'application/json' -Body $body -TimeoutSec 45 | ConvertTo-Json -Depth 12
