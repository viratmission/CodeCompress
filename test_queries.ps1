$baseUrl = "http://localhost:5000"
$repoId = 2
$sessionId = "mentor-session-" + [Guid]::NewGuid().ToString()

$queries = @(
    "I want authentication beginner to advanced.",
    "Teach me authentication from the beginning using this repository.",
    "Explain the login flow step by step.",
    "Explain AuthService.cs like I'm a beginner.",
    "What authentication features are actually implemented in this repository?",
    "What authentication features are missing?",
    "Show me the frontend-to-backend authentication flow.",
    "Quiz me on authentication.",
    "What should I learn next?",
    "Continue."
)

Write-Host "=== STARTING CODECOMPASS ASSISTANT VALIDATION ===" -ForegroundColor Cyan
Write-Host "Session ID: $sessionId" -ForegroundColor DarkGray

$results = @()
$qIndex = 1

foreach ($q in $queries) {
    Write-Host "`n--------------------------------------------------" -ForegroundColor Yellow
    Write-Host "Query ${qIndex}: '$q'" -ForegroundColor Green
    
    $body = @{
        question = $q
        questionType = "ask"
        sessionId = $sessionId
    } | ConvertTo-Json
    
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $resp = Invoke-RestMethod -Uri "$baseUrl/api/repositories/$repoId/assistant/ask" `
                                  -Method Post `
                                  -ContentType "application/json" `
                                  -Body $body `
                                  -TimeoutSec 60
        $stopwatch.Stop()
        
        Write-Host "Duration: $($stopwatch.ElapsedMilliseconds)ms" -ForegroundColor DarkGray
        Write-Host "Files cited: $($resp.references.Count)" -ForegroundColor Gray
        foreach ($r in $resp.references | Select-Object -First 4) {
            Write-Host "  - $($r.filePath)" -ForegroundColor DarkGray
        }
        
        $answerSnippet = if ($resp.answer.Length -gt 350) { $resp.answer.Substring(0, 350) + "..." } else { $resp.answer }
        Write-Host "`nAnswer Snippet:" -ForegroundColor White
        Write-Host $answerSnippet
        
        $results += [PSCustomObject]@{
            Index = $qIndex
            Question = $q
            FilesCount = $resp.references.Count
            AnswerLength = $resp.answer.Length
            FullAnswer = $resp.answer
        }
    }
    catch {
        $stopwatch.Stop()
        Write-Host "ERROR: $_" -ForegroundColor Red
        $results += [PSCustomObject]@{
            Index = $qIndex
            Question = $q
            FilesCount = 0
            AnswerLength = 0
            FullAnswer = "ERROR: $_"
        }
    }
    
    $qIndex++
}

# Save output to json
$results | ConvertTo-Json -Depth 5 | Out-File -FilePath "d:/CodeCompress/validation_output.json" -Encoding utf8
Write-Host "`n=== VALIDATION COMPLETED. Saved to validation_output.json ===" -ForegroundColor Cyan
