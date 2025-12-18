param(
    [string]$collection
)
if (-not $collection) { Write-Error "Collection name required"; exit 1 }
$gid = [guid]::NewGuid().ToString('N').ToLower()
# Build a zero vector of 1024 floats
$vector = 0..1023 | ForEach-Object { 0.0 }
$pointObj = @{ points = @( @{ id = 'test_chunk_1'; vector = $vector; payload = @{ project_id = $gid; file_path = 'test.cs'; start_line = 1; end_line = 2; language = 'csharp'; semantic_type = 'method'; semantic_name = 'Test' } } ) }
$outFile = Join-Path -Path $PSScriptRoot -ChildPath 'upsert.json'
$pointObj | ConvertTo-Json -Depth 6 | Out-File -FilePath $outFile -Encoding utf8
 try {
     $resp = Invoke-RestMethod -Uri ("http://localhost:6333/collections/" + $collection + "/points") -Method Put -InFile $outFile -ContentType 'application/json' -TimeoutSec 120 -ErrorAction Stop
     Write-Output "Upsert response: $($resp | ConvertTo-Json -Depth 3)"
 }
 catch {
     Write-Error "Failed to upsert points: $($_.Exception.Message)"
     throw
 }
 
Write-Output "Upserted point for project_id: $gid into collection: $collection"
$countResp = Invoke-RestMethod -Uri ("http://localhost:6333/collections/" + $collection + "/points/count") -Method Post -Body '{}' -ContentType 'application/json'
$countResp | ConvertTo-Json -Depth 5
Write-Output "Upserted point for project_id: $gid into collection: $collection"
Invoke-RestMethod -Uri ("http://localhost:6333/collections/" + $collection + "/points/count") -Method Post -Body '{}' -ContentType 'application/json' | ConvertTo-Json -Depth 5
