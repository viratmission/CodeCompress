$questions = @(
    "Explain the architecture of this project.",
    "I'm a beginner. Explain how this project works from the frontend to the database.",
    "How does authentication work in this project?",
    "Trace what happens when a user creates a todo.",
    "What files should a new backend developer read first and why?",
    "What is the impact of modifying todo item validation?"
)

$repoId = 2
$results = @()

foreach ($q in $questions) {
    Write-Host "=================================================="
    Write-Host "TESTING QUESTION: $q"
    Write-Host "=================================================="
    
    $qType = if ($q -like "*impact*") { "change-impact" } else { "ask" }
    $payload = @{
        question = $q
        questionType = $qType
    } | ConvertTo-Json

    try {
        $startTime = Get-Date
        $response = Invoke-RestMethod -Uri "http://localhost:5000/api/repositories/$repoId/assistant/ask" -Method Post -ContentType "application/json" -Body $payload
        $duration = ((Get-Date) - $startTime).TotalSeconds
        
        Write-Host "SUCCESS (took $($duration.ToString('F1'))s)"
        Write-Host "Files examined: $($response.context.filesExamined)"
        Write-Host "Modules examined: $($response.context.modulesExamined)"
        Write-Host "References count: $($response.references.Count)"
        Write-Host "Answer preview (first 400 chars):"
        Write-Host $response.answer.Substring(0, [Math]::Min(400, $response.answer.Length))
        Write-Host "..."
        
        $results += [PSCustomObject]@{
            Question = $q
            Success = $true
            Duration = $duration
            FilesExamined = $response.context.filesExamined
            ModulesExamined = $response.context.modulesExamined
            References = ($response.references | ForEach-Object { $_.filePath }) -join ", "
            Answer = $response.answer
        }
    }
    catch {
        Write-Host "ERROR: $_"
        $results += [PSCustomObject]@{
            Question = $q
            Success = $false
            Error = $_.ToString()
        }
    }
}

$results | ConvertTo-Json -Depth 6 | Out-File -FilePath "d:\CodeCompress\test_results.json" -Encoding utf8
Write-Host "All tests completed. Output saved to d:\CodeCompress\test_results.json"
