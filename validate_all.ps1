$repos = Invoke-RestMethod -Uri "http://localhost:5000/api/repositories"
$repo2 = $repos | Where-Object { $_.name -like "*Todo*" } | Select-Object -First 1
if (-not $repo2) { $repo2 = $repos | Where-Object { $_.id -eq 2 } }

Write-Host "=== Testing Multi-Module Repo: $($repo2.name) (ID $($repo2.id)) ==="

Write-Host "`n--- Backend Developer Onboarding ---"
$be = Invoke-RestMethod -Uri "http://localhost:5000/api/repositories/$($repo2.id)/onboarding/Backend%20Developer"
Write-Host "Title: $($be.title) | Steps: $($be.steps.Count) | Progress: $($be.progressPercentage)%"
foreach ($s in $be.steps) {
    Write-Host "  Step $($s.order): $($s.title) [$($s.status)] | Module: $($s.moduleName) (Layer: $($s.layer))"
}

Write-Host "`n--- Frontend Developer Onboarding ---"
$fe = Invoke-RestMethod -Uri "http://localhost:5000/api/repositories/$($repo2.id)/onboarding/Frontend%20Developer"
Write-Host "Title: $($fe.title) | Steps: $($fe.steps.Count) | Progress: $($fe.progressPercentage)%"
foreach ($s in $fe.steps) {
    Write-Host "  Step $($s.order): $($s.title) [$($s.status)] | Module: $($s.moduleName) (Layer: $($s.layer))"
}

Write-Host "`n--- Full Stack Developer Onboarding ---"
$fs = Invoke-RestMethod -Uri "http://localhost:5000/api/repositories/$($repo2.id)/onboarding/Full%20Stack%20Developer"
Write-Host "Title: $($fs.title) | Steps: $($fs.steps.Count) | Progress: $($fs.progressPercentage)%"
foreach ($s in $fs.steps) {
    Write-Host "  Step $($s.order): $($s.title) [$($s.status)] | Module: $($s.moduleName) (Layer: $($s.layer))"
}

Write-Host "`n--- Starter Tasks ---"
$tasks = Invoke-RestMethod -Uri "http://localhost:5000/api/repositories/$($repo2.id)/starter-tasks"
Write-Host "Found $($tasks.Count) starter tasks"
foreach ($t in $tasks) {
    Write-Host "  [$($t.difficulty)] $($t.title) (Module: $($t.relatedModuleName), Files: $($t.fileCount))"
}

Write-Host "`n--- Health & AI Provider ---"
$health = Invoke-RestMethod -Uri "http://localhost:5000/api/health"
Write-Host "Status: $($health.status) | Service: $($health.service) | AI Provider: $($health.aiProvider)"
