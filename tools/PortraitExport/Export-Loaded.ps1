$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path.Replace('\','/')
$ids = @('03','04','06','07','08','09','10','11','12','13','14','15','16','19','20','21','24','25','26','27','28','29','32','33','35','36','38','39')
$receipts = @()
foreach ($id in $ids) {
    $bundle = "assets_development_arts_ui_atlas_cn_resbundle_texture_character_character_$id.bundle"
    $command = "call PortraitExport.Entry.Run $bundle $root/artifacts/portrait-export-20261122/character_$id"
    $body = @{command=$command;target='logic';waitMs=1500} | ConvertTo-Json
    $r = Invoke-RestMethod 'http://127.0.0.1:8080/api/command' -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 30
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ($r.data.status -in @('queued','running')) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Pending receipt $($r.data.id); do not resubmit." }
        Start-Sleep -Milliseconds 250
        $r = Invoke-RestMethod "http://127.0.0.1:8080/api/commands/$($r.data.id)" -TimeoutSec 10
    }
    $receipts += [pscustomobject]@{character=$id;id=$r.data.id;status=$r.data.status;error=$r.data.error}
    $receipts | ConvertTo-Json -Depth 8 | Set-Content "$root/artifacts/portrait-export-20261122/receipts.json" -Encoding UTF8
    if ($r.data.status -ne 'completed') { throw "Failed receipt $($r.data.id)" }
    Write-Output "character_$id : completed"
}
