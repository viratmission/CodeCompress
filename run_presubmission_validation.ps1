$baseUrl = "http://localhost:5000"
$repoId = 2
$sessionId = "presubmit-session-" + [Guid]::NewGuid().ToString()

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "     CODECOMPASS PRE-SUBMISSION AUTOMATED VALIDATION     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Base URL:   $baseUrl" -ForegroundColor DarkGray
Write-Host "Repo ID:    $repoId" -ForegroundColor DarkGray
Write-Host "Session ID: $sessionId" -ForegroundColor DarkGray

$testResults = [System.Collections.Generic.List[PSCustomObject]]::new()

function Record-Test($name, $passed, $details) {
    $statusStr = if ($passed) { "[PASS]" } else { "[FAIL]" }
    $color = if ($passed) { "Green" } else { "Red" }
    Write-Host "$statusStr $name : $details" -ForegroundColor $color
    $testResults.Add([PSCustomObject]@{
        TestName = $name
        Passed   = $passed
        Details  = $details
    })
}

# ── 1. Health Endpoint ───────────────────────────────────────────────────────
try {
    $h = Invoke-RestMethod "$baseUrl/api/health"
    $ok = ($h.status -eq "healthy") -and ($h.watsonxConfigured -eq $true)
    Record-Test "Health Endpoint" $ok "Status: $($h.status), WatsonX: $($h.watsonxConfigured), Provider: $($h.aiProvider)"
} catch {
    Record-Test "Health Endpoint" $false $_.Message
}

# ── 2. Repositories List ─────────────────────────────────────────────────────
try {
    $repos = Invoke-RestMethod "$baseUrl/api/repositories"
    $ok = ($repos.Count -gt 0)
    $activeRepo = $repos | Where-Object { $_.id -eq $repoId } | Select-Object -First 1
    Record-Test "Repository Listing" $ok "Found $($repos.Count) repositories. Active repo: $($activeRepo.name) ($($activeRepo.primaryLanguage))"
} catch {
    Record-Test "Repository Listing" $false $_.Message
}

# ── 3. Repository Analysis Metrics ───────────────────────────────────────────
try {
    $analysis = Invoke-RestMethod "$baseUrl/api/repositories/$repoId/analysis"
    $ok = ($analysis.totalFiles -gt 0) -and ($analysis.totalModules -gt 0)
    Record-Test "Repository Analysis" $ok "Files: $($analysis.totalFiles), Modules: $($analysis.totalModules), Deps: $($analysis.totalDependencies), Langs: $($analysis.languageStats.Count)"
} catch {
    Record-Test "Repository Analysis" $false $_.Message
}

# ── 4. Architecture Graph ────────────────────────────────────────────────────
$firstModuleId = $null
try {
    $arch = Invoke-RestMethod "$baseUrl/api/repositories/$repoId/architecture"
    $ok = ($arch.nodes.Count -gt 0)
    if ($arch.nodes.Count -gt 0) { $firstModuleId = $arch.nodes[0].id }
    Record-Test "Architecture Graph" $ok "Nodes: $($arch.nodes.Count), Links: $($arch.links.Count), Layers: $($arch.layers.Count)"
} catch {
    Record-Test "Architecture Graph" $false $_.Message
}

# ── 5. Module Detail ─────────────────────────────────────────────────────────
try {
    if ($firstModuleId) {
        $mod = Invoke-RestMethod "$baseUrl/api/repositories/$repoId/modules/$firstModuleId"
        Record-Test "Module Detail" ($null -ne $mod.name) "Module '$($mod.name)' [Type: $($mod.moduleType), Files: $($mod.files.Count)]"
    } else {
        Record-Test "Module Detail" $false "No module ID available"
    }
} catch {
    Record-Test "Module Detail" $false $_.Message
}

# ── 6. Onboarding All 4 Roles ────────────────────────────────────────────────
$roles = @("Backend Developer", "Frontend Developer", "Full Stack Developer", "QA Engineer")
$firstOnboardingId = $null
$firstStepId = $null
foreach ($role in $roles) {
    try {
        $encodedRole = [System.Uri]::EscapeDataString($role)
        $p = Invoke-RestMethod "$baseUrl/api/repositories/$repoId/onboarding/$encodedRole"
        $ok = ($p.steps.Count -gt 0)
        if (-not $firstOnboardingId) {
            $firstOnboardingId = $p.userOnboardingId
            $firstStepId = $p.steps[0].id
        }
        Record-Test "Onboarding Path ($role)" $ok "Steps: $($p.steps.Count), Progress: $($p.progressPercentage)%"
    } catch {
        Record-Test "Onboarding Path ($role)" $false $_.Message
    }
}

# ── 7. Onboarding Step Detail & Step Completion Persistence ──────────────────
try {
    if ($firstOnboardingId -and $firstStepId) {
        $stepDetail = Invoke-RestMethod "$baseUrl/api/onboarding/$firstOnboardingId/steps/$firstStepId"
        $hasLearn = -not [string]::IsNullOrWhiteSpace($stepDetail.whatYouWillLearn)
        Record-Test "Onboarding Step Detail" $hasLearn "Title: '$($stepDetail.title)', Files: $($stepDetail.relevantFiles.Count)"
        
        $comp = Invoke-RestMethod -Uri "$baseUrl/api/onboarding/$firstOnboardingId/steps/$firstStepId/complete" -Method Post
        Record-Test "Onboarding Step Complete" ($comp.status -eq "completed") "New progress: $($comp.progressPercentage)%"
    }
} catch {
    Record-Test "Onboarding Step Detail/Complete" $false $_.Message
}

# ── 8. Starter Tasks ─────────────────────────────────────────────────────────
$firstTaskId = $null
try {
    $tasks = Invoke-RestMethod "$baseUrl/api/repositories/$repoId/starter-tasks"
    $ok = ($tasks.Count -gt 0)
    if ($ok) { $firstTaskId = $tasks[0].id }
    Record-Test "Starter Tasks List" $ok "Found $($tasks.Count) tasks. Top task: '$($tasks[0].title)' ($($tasks[0].difficulty))"
} catch {
    Record-Test "Starter Tasks List" $false $_.Message
}

# ── 9. Starter Task Detail ───────────────────────────────────────────────────
try {
    if ($firstTaskId) {
        $taskDetail = Invoke-RestMethod "$baseUrl/api/starter-tasks/$firstTaskId"
        $ok = ($taskDetail.changeImpact.Count -gt 0)
        Record-Test "Starter Task Detail" $ok "Impact items: $($taskDetail.changeImpact.Count), Relevant files: $($taskDetail.relevantFiles.Count)"
    }
} catch {
    Record-Test "Starter Task Detail" $false $_.Message
}

# ── 10. Negative Tests ───────────────────────────────────────────────────────
# A. Invalid Repo ID
try {
    $r = Invoke-RestMethod "$baseUrl/api/repositories/999999/analysis" -ErrorAction Stop
    Record-Test "Negative: Invalid Repo ID" $false "Expected 404 but got success"
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    Record-Test "Negative: Invalid Repo ID" ($status -eq 404) "Correctly returned HTTP $status"
}

# B. Invalid Module ID
try {
    $r = Invoke-RestMethod "$baseUrl/api/repositories/$repoId/modules/999999" -ErrorAction Stop
    Record-Test "Negative: Invalid Module ID" $false "Expected 404 but got success"
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    Record-Test "Negative: Invalid Module ID" ($status -eq 404) "Correctly returned HTTP $status"
}

# C. Invalid Starter Task ID
try {
    $r = Invoke-RestMethod "$baseUrl/api/starter-tasks/999999" -ErrorAction Stop
    Record-Test "Negative: Invalid Task ID" $false "Expected 404 but got success"
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    Record-Test "Negative: Invalid Task ID" ($status -eq 404) "Correctly returned HTTP $status"
}

# D. Invalid Onboarding Step
try {
    $r = Invoke-RestMethod "$baseUrl/api/onboarding/999999/steps/999999" -ErrorAction Stop
    Record-Test "Negative: Invalid Step ID" $false "Expected 404 but got success"
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    Record-Test "Negative: Invalid Step ID" ($status -eq 404) "Correctly returned HTTP $status"
}

# E. Empty Assistant Question
try {
    $body = @{ question = ""; questionType = "ask" } | ConvertTo-Json
    $r = Invoke-RestMethod -Uri "$baseUrl/api/repositories/$repoId/assistant/ask" -Method Post -ContentType "application/json" -Body $body -ErrorAction Stop
    Record-Test "Negative: Empty Question" $false "Expected 400 but got success"
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    Record-Test "Negative: Empty Question" ($status -eq 400) "Correctly returned HTTP $status"
}

# F. Invalid / Non-GitHub URL
try {
    $body = @{ gitUrl = "https://gitlab.com/owner/repo" } | ConvertTo-Json
    $r = Invoke-RestMethod -Uri "$baseUrl/api/repositories/analyze" -Method Post -ContentType "application/json" -Body $body -ErrorAction Stop
    Record-Test "Negative: Non-GitHub URL" $false "Expected 400 but got success"
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    Record-Test "Negative: Non-GitHub URL" ($status -eq 400) "Correctly returned HTTP $status"
}

# G. Unsafe URL / Command Injection attempt
try {
    $body = @{ gitUrl = "https://github.com/owner/repo;rm -rf /" } | ConvertTo-Json
    $r = Invoke-RestMethod -Uri "$baseUrl/api/repositories/analyze" -Method Post -ContentType "application/json" -Body $body -ErrorAction Stop
    Record-Test "Negative: Unsafe URL Injection" $false "Expected 400 but got success"
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    Record-Test "Negative: Unsafe URL Injection" ($status -eq 400) "Correctly returned HTTP $status"
}

# ── 11. Live AI Assistant: 5 Required Queries + Continuation ───────────────────
$aiQueries = @(
    @{ Q = "Explain the architecture of this project."; Type = "ask"; Name = "AI: Architecture Explanation" },
    @{ Q = "Teach me authentication from beginner to advanced using this repository."; Type = "ask"; Name = "AI: Authentication Roadmap" },
    @{ Q = "Trace a request from the frontend to the backend."; Type = "ask"; Name = "AI: Request Flow Trace" },
    @{ Q = "What happens if I modify this feature?"; Type = "change-impact"; Name = "AI: Change Impact Assessment" },
    @{ Q = "Explain this project like I am a new developer joining the team."; Type = "ask"; Name = "AI: Developer Onboarding Overview" },
    @{ Q = "Continue."; Type = "ask"; Name = "AI: Session Continuation" }
)

Write-Host "`n--- Running AI Queries against IBM watsonx.ai ---" -ForegroundColor Yellow
$aiResponses = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($item in $aiQueries) {
    Write-Host "Calling watsonx: '$($item.Q)'..." -ForegroundColor DarkGray
    $body = @{
        question = $item.Q
        questionType = $item.Type
        sessionId = $sessionId
    } | ConvertTo-Json
    
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $resp = Invoke-RestMethod -Uri "$baseUrl/api/repositories/$repoId/assistant/ask" `
                                  -Method Post `
                                  -ContentType "application/json" `
                                  -Body $body `
                                  -TimeoutSec 60
        $sw.Stop()
        
        $hasAnswer = ($resp.answer.Length -gt 100)
        $hasRefs   = ($resp.references.Count -gt 0)
        $summary = "Elapsed: $($sw.ElapsedMilliseconds)ms, AnswerLen: $($resp.answer.Length), Refs: $($resp.references.Count)"
        Record-Test $item.Name ($hasAnswer -and $hasRefs) $summary
        
        $aiResponses.Add([PSCustomObject]@{
            Query = $item.Q
            Type = $item.Type
            Answer = $resp.answer
            References = $resp.references
        })
    } catch {
        $sw.Stop()
        Record-Test $item.Name $false "Error: $_"
    }
}

# Save full results
@{
    Tests = $testResults
    AiResponses = $aiResponses
} | ConvertTo-Json -Depth 6 | Out-File -FilePath "d:/CodeCompress/presubmission_results.json" -Encoding utf8

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " Validation complete. Saved to presubmission_results.json " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
