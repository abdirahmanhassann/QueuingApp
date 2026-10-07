# Packs QueueingApp + QueuingAppFE (Line) and uploads them to your AWS storage (link valid until ~21:33 on 7 Oct).
$ErrorActionPreference = "Stop"
$out = Join-Path $env:TEMP "line-src.tgz"
Set-Location "C:\Users\abdi1\source\repos"
tar.exe -czf $out --exclude=node_modules --exclude=bin --exclude=obj --exclude=.git --exclude=.vs --exclude=.vercel --exclude=.tanstack --exclude=.output --exclude=screenshots --exclude=attachments QueueingApp QueuingAppFE
Write-Host ("Packed {0:N1} MB, uploading..." -f ((Get-Item $out).Length/1MB))
$code = curl.exe -s -o NUL -w "%{http_code}" -T $out "https://REMOVED-PRESIGNED-URL"
if ($code -eq "200") { Write-Host "UPLOAD OK (HTTP 200) - tell Claude it's done" -ForegroundColor Green } else { Write-Host "UPLOAD FAILED (HTTP $code) - tell Claude" -ForegroundColor Red }
Read-Host "Press Enter to close"
