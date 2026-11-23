$ErrorActionPreference = 'Stop'
$moves = Get-Content -LiteralPath 'Assets/3DBattle/Authoring~/CharacterAssetMovePlan.json' -Raw | ConvertFrom-Json
$parents = @($moves.from | ForEach-Object { [IO.Path]::GetDirectoryName($_).Replace('\','/') } | Sort-Object -Unique)
$updated = @()
foreach ($file in (Get-ChildItem -LiteralPath 'Assets/3DBattle/Authoring~' -Filter '*.cs' -File)) {
    if ($file.Name -eq 'OrganizeBattleCharacterAssets.cs') { continue }
    $original = [IO.File]::ReadAllText($file.FullName)
    $text = $original
    $usesFolder = @($parents | Where-Object { $original.Contains($_) }).Count -gt 0
    foreach ($move in $moves) { $text = $text.Replace($move.from, $move.to) }
    if ($usesFolder) {
        $text = $text.Replace('AssetDatabase.LoadAssetAtPath<', 'BattleCharacterAssetPaths.Load<')
        $text = $text.Replace('AssetDatabase.LoadAllAssetsAtPath(', 'BattleCharacterAssetPaths.LoadAll(')
        $text = $text.Replace('AssetDatabase.LoadMainAssetAtPath(', 'BattleCharacterAssetPaths.LoadMain(')
    }
    $text = $text.Replace('UnityEditor.BattleCharacterAssetPaths.', 'BattleCharacterAssetPaths.')
    if ($text -cne $original) {
        [IO.File]::WriteAllText($file.FullName, $text, [Text.UTF8Encoding]::new($false))
        $updated += $file.Name
    }
}
$log = 'Assets/3DBattle/Authoring~/CharacterAssetAuthoringUpdates.json'
$previous = if (Test-Path -LiteralPath $log) { @(Get-Content -LiteralPath $log -Raw | ConvertFrom-Json) } else { @() }
@($previous + $updated | Sort-Object -Unique) | ConvertTo-Json | Set-Content -LiteralPath $log -Encoding utf8
[pscustomobject]@{updatedFiles=$updated.Count; scope='Assets/3DBattle/Authoring~'; deleted=0}
