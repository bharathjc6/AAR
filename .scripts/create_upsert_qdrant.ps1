$gid = [guid]::NewGuid().ToString('N').ToLower()
$collection = "aar_test_$($gid)_vectors"
Write-Output "Collection: $collection"
$create = @{ vectors = @{ size = 1024; distance = 'Cosine' } } | ConvertTo-Json -Depth 5
Invoke-RestMethod -Uri ("http://localhost:6333/collections/" + $collection) -Method Put -Body $create -ContentType 'application/json'
Write-Output 'Collection created'
$vector = 0..1023 | ForEach-Object { 0.0 }
$point = @{ points = @( @{ id = 'test_chunk_1'; vector = $vector; payload = @{ project_id = $gid; file_path = 'test.cs'; start_line=1; end_line=2; language='csharp'; semantic_type='method'; semantic_name='Test' } } ) } | ConvertTo-Json -Depth 6
Invoke-RestMethod -Uri ("http://localhost:6333/collections/" + $collection + "/points") -Method Put -Body $point -ContentType 'application/json'
Write-Output 'Upsert complete'
$count = Invoke-RestMethod -Uri ("http://localhost:6333/collections/" + $collection + "/points/count") -Method Post -Body '{}' -ContentType 'application/json'
$count | ConvertTo-Json -Depth 5
