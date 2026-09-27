import { useState, useEffect } from 'react'

// ── Types ──────────────────────────────────────────────────────────────────
export interface Repository {
  id: number
  name: string
  gitUrl: string
  primaryLanguage: string
}

export interface RepoModuleDto {
  id: number
  name: string
  path: string
  moduleType: string
  description: string
  layer: string
  fileCount: number
}

export interface RepoFileDto {
  id: number
  filePath: string
  fileName: string
  extension: string
  language: string
  fileSize: number
  isDirectory: boolean
}

export interface RepoDepDto {
  id: number
  sourceFilePath: string
  targetFilePath: string
  dependencyType: string
  importStatement: string
}

export interface ChangeImpactItemDto {
  filePath: string
  moduleName: string
  impact: string
  reason: string
}

export interface OnboardingStepSummary {
  id: number
  title: string
  description: string
  order: number
  stepType: string
  moduleId?: number | null
  moduleName?: string
  modulePath?: string
  layer?: string
  status: string
  completedAt?: string | null
}

export interface OnboardingPathData {
  id: number
  repositoryId: number
  role: string
  title: string
  description: string
  userOnboardingId: number
  progressPercentage: number
  steps: OnboardingStepSummary[]
  relatedModules: RepoModuleDto[]
  relevantFiles: RepoFileDto[]
}

export interface OnboardingStepDetail {
  stepId: number
  userOnboardingId: number
  title: string
  whatYouWillLearn: string
  whyItMatters: string
  stepType: string
  relatedModule?: RepoModuleDto | null
  relevantFiles: RepoFileDto[]
  dependencies: RepoDepDto[]
  aiExplanation: string
  status: string
  completedAt?: string | null
}

export interface StarterTaskSummary {
  id: number
  repositoryId: number
  title: string
  description: string
  difficulty: string
  reason: string
  relatedModuleId?: number | null
  relatedModuleName?: string
  fileCount: number
}

export interface StarterTaskDetail {
  id: number
  repositoryId: number
  title: string
  description: string
  difficulty: string
  reasonForRecommendation: string
  relatedModule?: RepoModuleDto | null
  relevantFiles: RepoFileDto[]
  knownDependencies: RepoDepDto[]
  suggestedFirstStep: string
  existingPattern: string
  changeImpact: ChangeImpactItemDto[]
}

const ROLES = [
  {
    id: 'Backend Developer',
    title: 'Backend Developer',
    icon: '⚙️',
    description: 'Server architectures, Web APIs, data access, business logic, and backend contribution.',
    badgeColor: '#10b981',
  },
  {
    id: 'Frontend Developer',
    title: 'Frontend Developer',
    icon: '🎨',
    description: 'UI components, client state management, styling, client-server integration, and frontend workflows.',
    badgeColor: '#3b82f6',
  },
  {
    id: 'Full Stack Developer',
    title: 'Full Stack Developer',
    icon: '🚀',
    description: 'End-to-end data flow, full stack modules, shared contracts, and cross-tier feature contribution.',
    badgeColor: '#8b5cf6',
  },
  {
    id: 'QA Engineer',
    title: 'QA Engineer',
    icon: '🧪',
    description: 'System test coverage, integration testing surfaces, API contracts, and quality verification.',
    badgeColor: '#f59e0b',
  },
]

const LANG_COLOURS: Record<string, string> = {
  'C#': '#178600', TypeScript: '#3178c6', JavaScript: '#f1e05a',
  Python: '#3572A5', Java: '#b07219', HTML: '#e34c26', CSS: '#563d7c',
  SQL: '#e38c00', JSON: '#a52a2a', YAML: '#cb171e', Markdown: '#083fa1',
}
const langColour = (lang: string) => LANG_COLOURS[lang] ?? '#8b949e'
const fmtBytes = (b: number) => b < 1024 ? `${b} B` : b < 1048576 ? `${(b/1024).toFixed(1)} KB` : `${(b/1048576).toFixed(1)} MB`

interface OnboardingPageProps {
  initialViewMode?: 'onboarding' | 'starter-tasks'
  initialRepoId?: number | null
  initialRole?: string
  onRoleChange?: (role: string) => void
  onNavigateToArchitecture?: (repoId: number, moduleName?: string) => void
  onNavigateToAssistant?: (repoId: number, question: string, questionType?: 'ask' | 'change-impact') => void
}

export default function OnboardingPage({
  initialViewMode = 'onboarding',
  initialRepoId,
  initialRole,
  onRoleChange,
  onNavigateToArchitecture,
  onNavigateToAssistant,
}: OnboardingPageProps) {
  // Repositories
  const [repositories, setRepositories] = useState<Repository[]>([])
  const [selectedRepoId, setSelectedRepoId] = useState<number | null>(initialRepoId ?? null)
  
  // Role & Path
  const [selectedRole, setSelectedRole] = useState<string>(() => {
    return initialRole || localStorage.getItem('codecompass_role') || 'Backend Developer'
  })
  const [isChangingRole, setIsChangingRole] = useState<boolean>(false)

  // Onboarding data
  const [onboardingPath, setOnboardingPath] = useState<OnboardingPathData | null>(null)
  const [loadingPath, setLoadingPath] = useState<boolean>(false)
  const [pathError, setPathError] = useState<string>('')

  // Step modal
  const [activeStepId, setActiveStepId] = useState<number | null>(null)
  const [stepDetail, setStepDetail] = useState<OnboardingStepDetail | null>(null)
  const [loadingStep, setLoadingStep] = useState<boolean>(false)
  const [completingStep, setCompletingStep] = useState<boolean>(false)

  // Starter Tasks
  const [starterTasks, setStarterTasks] = useState<StarterTaskSummary[]>([])
  const [loadingTasks, setLoadingTasks] = useState<boolean>(false)
  const [activeTaskId, setActiveTaskId] = useState<number | null>(null)
  const [taskDetail, setTaskDetail] = useState<StarterTaskDetail | null>(null)
  const [loadingTaskDetail, setLoadingTaskDetail] = useState<boolean>(false)

  // Active view tab (inside onboarding page or from main nav)
  const [activeTab, setActiveTab] = useState<'path' | 'tasks'>(
    initialViewMode === 'starter-tasks' ? 'tasks' : 'path'
  )

  // Sync prop changes
  useEffect(() => {
    if (initialRepoId) setSelectedRepoId(initialRepoId)
  }, [initialRepoId])

  useEffect(() => {
    if (initialRole) setSelectedRole(initialRole)
  }, [initialRole])

  useEffect(() => {
    setActiveTab(initialViewMode === 'starter-tasks' ? 'tasks' : 'path')
  }, [initialViewMode])

  // Load repositories on mount
  useEffect(() => {
    fetch('/api/repositories')
      .then(r => (r.ok ? r.json() : []))
      .then((repos: Repository[]) => {
        setRepositories(repos)
        if (repos.length > 0 && !selectedRepoId) {
          setSelectedRepoId(repos[0].id)
        }
      })
      .catch(() => {})
  }, [])

  // Persist role change
  const handleSelectRole = (role: string) => {
    setSelectedRole(role)
    localStorage.setItem('codecompass_role', role)
    if (onRoleChange) onRoleChange(role)
    setIsChangingRole(false)
  }

  // Fetch onboarding path when repo or role changes
  useEffect(() => {
    if (!selectedRepoId || !selectedRole) return
    setLoadingPath(true)
    setPathError('')
    setOnboardingPath(null)

    fetch(`/api/repositories/${selectedRepoId}/onboarding/${encodeURIComponent(selectedRole)}`)
      .then(async r => {
        if (!r.ok) {
          const err = await r.json().catch(() => ({}))
          throw new Error(err.error || 'Failed to generate onboarding path')
        }
        return r.json()
      })
      .then((data: OnboardingPathData) => {
        setOnboardingPath(data)
      })
      .catch(err => {
        setPathError(err.message || 'Error loading onboarding path.')
      })
      .finally(() => setLoadingPath(false))
  }, [selectedRepoId, selectedRole])

  // Fetch starter tasks when repo changes
  useEffect(() => {
    if (!selectedRepoId) return
    setLoadingTasks(true)
    fetch(`/api/repositories/${selectedRepoId}/starter-tasks`)
      .then(r => (r.ok ? r.json() : []))
      .then((tasks: StarterTaskSummary[]) => {
        setStarterTasks(tasks)
      })
      .catch(() => setStarterTasks([]))
      .finally(() => setLoadingTasks(false))
  }, [selectedRepoId])

  // Fetch step detail when activeStepId changes
  useEffect(() => {
    if (!activeStepId || !onboardingPath) {
      setStepDetail(null)
      return
    }
    setLoadingStep(true)
    fetch(`/api/onboarding/${onboardingPath.id}/steps/${activeStepId}`)
      .then(r => (r.ok ? r.json() : null))
      .then((detail: OnboardingStepDetail | null) => {
        setStepDetail(detail)
      })
      .catch(() => setStepDetail(null))
      .finally(() => setLoadingStep(false))
  }, [activeStepId, onboardingPath])

  // Fetch starter task detail when activeTaskId changes
  useEffect(() => {
    if (!activeTaskId) {
      setTaskDetail(null)
      return
    }
    setLoadingTaskDetail(true)
    fetch(`/api/starter-tasks/${activeTaskId}`)
      .then(r => (r.ok ? r.json() : null))
      .then((detail: StarterTaskDetail | null) => {
        setTaskDetail(detail)
      })
      .catch(() => setTaskDetail(null))
      .finally(() => setLoadingTaskDetail(false))
  }, [activeTaskId])

  // Handle Mark Step Complete
  const handleCompleteStep = async (stepId: number) => {
    if (!onboardingPath) return
    setCompletingStep(true)
    try {
      const res = await fetch(`/api/onboarding/${onboardingPath.id}/steps/${stepId}/complete`, {
        method: 'POST',
      })
      if (res.ok) {
        const updateData = await res.json()
        setOnboardingPath(prev => {
          if (!prev) return null
          return {
            ...prev,
            progressPercentage: updateData.progressPercentage,
            steps: prev.steps.map(s =>
              s.id === stepId ? { ...s, status: 'completed', completedAt: updateData.completedAt } : s
            ),
          }
        })
        if (stepDetail && stepDetail.stepId === stepId) {
          setStepDetail({ ...stepDetail, status: 'completed', completedAt: updateData.completedAt })
        }
      }
    } catch (e) {
      console.error(e)
    } finally {
      setCompletingStep(false)
    }
  }

  const currentRepo = repositories.find(r => r.id === selectedRepoId)
  const completedCount = onboardingPath?.steps.filter(s => s.status === 'completed').length || 0
  const totalCount = onboardingPath?.steps.length || 0
  const progressPct = onboardingPath?.progressPercentage || 0

  return (
    <div className="onboarding-container" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {/* ── Page Header ── */}
      <header className="page-header">
        <div>
          <h1 className="page-title">Developer Onboarding</h1>
          <p className="page-subtitle">
            Structured, architecture-grounded learning paths and beginner-friendly starter tasks
          </p>
        </div>

        {/* Repository Selector */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <label style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-muted)' }}>Repository:</label>
          <select
            className="arch-select"
            value={selectedRepoId || ''}
            onChange={e => setSelectedRepoId(Number(e.target.value))}
          >
            {repositories.map(r => (
              <option key={r.id} value={r.id}>
                {r.name} ({r.primaryLanguage || 'Codebase'})
              </option>
            ))}
          </select>
        </div>
      </header>

      {/* ── Role Banner & Selector ── */}
      <div className="role-banner">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 16 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
            <div
              style={{
                width: 44,
                height: 44,
                borderRadius: 8,
                background: 'rgba(255, 255, 255, 0.06)',
                border: '1px solid var(--border-default)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: 22,
              }}
            >
              {ROLES.find(r => r.id === selectedRole)?.icon || '🧑‍💻'}
            </div>
            <div>
              <div style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: '0.06em', color: 'var(--text-muted)', fontWeight: 600 }}>
                Active Developer Track
              </div>
              <div style={{ fontSize: 18, fontWeight: 700, color: 'var(--text-primary)', display: 'flex', alignItems: 'center', gap: 8 }}>
                {selectedRole}
                <span
                  style={{
                    fontSize: 10.5,
                    padding: '1px 8px',
                    borderRadius: 12,
                    background: 'rgba(56, 139, 253, 0.15)',
                    color: '#79c0ff',
                    border: '1px solid rgba(56, 139, 253, 0.3)',
                    fontWeight: 600,
                  }}
                >
                  Tailored Curriculum
                </span>
              </div>
              <div style={{ fontSize: 12.5, color: 'var(--text-secondary)', marginTop: 2 }}>
                {ROLES.find(r => r.id === selectedRole)?.description}
              </div>
            </div>
          </div>

          <button
            className="btn-secondary"
            onClick={() => setIsChangingRole(!isChangingRole)}
          >
            {isChangingRole ? 'Cancel' : 'Switch Role ⇄'}
          </button>
        </div>

        {/* Role Selector Grid */}
        {isChangingRole && (
          <div className="role-grid">
            {ROLES.map(role => {
              const isCurrent = role.id === selectedRole
              return (
                <div
                  key={role.id}
                  onClick={() => handleSelectRole(role.id)}
                  className={`role-card-opt ${isCurrent ? 'role-card-opt--selected' : ''}`}
                >
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
                    <span style={{ fontSize: 18 }}>{role.icon}</span>
                    <span style={{ fontWeight: 600, fontSize: 13, color: isCurrent ? '#58a6ff' : 'var(--text-primary)' }}>
                      {role.title}
                    </span>
                  </div>
                  <p style={{ fontSize: 11.5, color: 'var(--text-muted)', lineHeight: 1.4 }}>{role.description}</p>
                </div>
              )
            })}
          </div>
        )}
      </div>

      {/* ── Progress Dashboard Header ── */}
      {onboardingPath && (
        <div className="card">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 16 }}>
            <div>
              <div className="card-label">Onboarding Readiness</div>
              <h2 style={{ fontSize: 18, fontWeight: 700, color: 'var(--text-primary)', marginTop: 2 }}>
                {onboardingPath.title}
              </h2>
              <div style={{ fontSize: 12.5, color: 'var(--text-secondary)', marginTop: 3 }}>
                Repository: <strong style={{ color: 'var(--text-primary)' }}>{currentRepo?.name}</strong> • Completed {completedCount} of {totalCount} steps
              </div>
            </div>

            {/* Visual Progress Bar & Badge */}
            <div style={{ minWidth: 260 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                <span style={{ fontSize: 11.5, fontWeight: 600, color: 'var(--text-muted)' }}>Progress</span>
                <span style={{ fontSize: 15, fontWeight: 700, color: progressPct === 100 ? '#3fb950' : '#58a6ff' }}>
                  {progressPct}% Completed
                </span>
              </div>
              <div style={{ height: 8, width: '100%', background: 'var(--border-default)', borderRadius: 4, overflow: 'hidden' }}>
                <div
                  style={{
                    height: '100%',
                    width: `${progressPct}%`,
                    background: progressPct === 100 ? '#238636' : 'linear-gradient(90deg, #1f6feb, #388bfd)',
                    borderRadius: 4,
                    transition: 'width 0.4s ease-in-out',
                  }}
                />
              </div>
              <div style={{ fontSize: 10.5, fontFamily: 'var(--font-mono)', color: 'var(--text-muted)', marginTop: 4, textAlign: 'right' }}>
                {`[${'█'.repeat(Math.round(progressPct / 10))}${'░'.repeat(10 - Math.round(progressPct / 10))}]`}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ── Sub Navigation Tabs ── */}
      <div className="tab-bar">
        <button
          className={`tab-btn ${activeTab === 'path' ? 'tab-btn--active' : ''}`}
          onClick={() => setActiveTab('path')}
        >
          <span>🗺️ Learning Path</span>
          <span className="arch-badge" style={{ marginLeft: 6 }}>{onboardingPath?.steps.length || 0}</span>
        </button>

        <button
          className={`tab-btn ${activeTab === 'tasks' ? 'tab-btn--active' : ''}`}
          onClick={() => setActiveTab('tasks')}
        >
          <span>🎯 Starter Tasks</span>
          <span className="arch-badge" style={{ marginLeft: 6 }}>{starterTasks.length}</span>
        </button>
      </div>

      {/* ── Content View ── */}
      {loadingPath ? (
        <div className="empty-state">
          <div style={{ fontSize: 24, marginBottom: 8 }}>⚡</div>
          <div style={{ fontWeight: 600, fontSize: 14 }}>Generating role-tailored onboarding path...</div>
          <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>Analyzing modules, architecture layers, and role requirements</div>
        </div>
      ) : pathError ? (
        <div className="alert alert--error">
          <strong>Unable to load onboarding path:</strong> {pathError}
        </div>
      ) : activeTab === 'path' ? (
        /* ══════════════════════════════════════════════════════════════════ */
        /* TAB 1: LEARNING PATH STEPS                                         */
        /* ══════════════════════════════════════════════════════════════════ */
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {onboardingPath?.steps.map((step, idx) => {
            const isCompleted = step.status === 'completed'
            const isNext = !isCompleted && onboardingPath.steps.findIndex(s => s.status !== 'completed') === idx

            return (
              <div
                key={step.id}
                onClick={() => setActiveStepId(step.id)}
                className={`step-card ${isNext ? 'step-card--current' : isCompleted ? 'step-card--completed' : ''}`}
              >
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: 14, flex: 1 }}>
                  {/* Status Circle */}
                  <div
                    className={`step-circle ${
                      isCompleted
                        ? 'step-circle--completed'
                        : isNext
                        ? 'step-circle--current'
                        : 'step-circle--upcoming'
                    }`}
                  >
                    {isCompleted ? '✓' : isNext ? '→' : '○'}
                  </div>

                  {/* Step Info */}
                  <div style={{ flex: 1 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                      <span style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-muted)' }}>
                        Step {step.order}
                      </span>
                      <h3 style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
                        {step.title}
                      </h3>
                      {step.stepType && (
                        <span className="type-badge" style={{ textTransform: 'capitalize' }}>
                          {step.stepType}
                        </span>
                      )}
                      {step.moduleName && (
                        <span style={{ fontSize: 11, padding: '1px 8px', borderRadius: 10, background: 'rgba(56, 139, 253, 0.1)', color: '#79c0ff', border: '1px solid rgba(56, 139, 253, 0.25)' }}>
                          📦 {step.moduleName}
                        </span>
                      )}
                      {step.layer && (
                        <span style={{ fontSize: 11, padding: '1px 8px', borderRadius: 10, background: 'rgba(210, 153, 34, 0.12)', color: '#f2cc60', border: '1px solid rgba(210, 153, 34, 0.25)' }}>
                          Layer: {step.layer}
                        </span>
                      )}
                    </div>

                    <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginTop: 4, marginBottom: 0, lineHeight: 1.45 }}>
                      {step.description}
                    </p>
                  </div>
                </div>

                {/* Right Action */}
                <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexShrink: 0 }}>
                  {isCompleted ? (
                    <span style={{ padding: '4px 10px', borderRadius: 6, background: 'rgba(46, 160, 67, 0.15)', color: '#3fb950', border: '1px solid rgba(46, 160, 67, 0.3)', fontSize: 12, fontWeight: 700 }}>
                      Completed ✓
                    </span>
                  ) : (
                    <button
                      className={isNext ? 'btn-accent' : 'btn-secondary'}
                      onClick={e => {
                        e.stopPropagation()
                        setActiveStepId(step.id)
                      }}
                    >
                      {isNext ? 'Start Step →' : 'View Details'}
                    </button>
                  )}
                </div>
              </div>
            )
          })}
        </div>
      ) : (
        /* ══════════════════════════════════════════════════════════════════ */
        /* TAB 2: STARTER TASKS RECOMMENDATION                                */
        /* ══════════════════════════════════════════════════════════════════ */
        <div>
          <div style={{ marginBottom: 14 }}>
            <h2 className="section-title">Recommended Starter Tasks</h2>
            <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', marginTop: 2 }}>
              Codebase intelligence identified these safe beginner tasks with low dependency blast radius.
            </p>
          </div>

          {loadingTasks ? (
            <div className="empty-state">Finding suitable starter tasks...</div>
          ) : starterTasks.length === 0 ? (
            <div className="empty-state">
              <div style={{ fontSize: 24, marginBottom: 6 }}>🔍</div>
              <div>No suitable starter task could be identified automatically.</div>
            </div>
          ) : (
            <div className="starter-task-grid">
              {starterTasks.map(task => (
                <div
                  key={task.id}
                  onClick={() => setActiveTaskId(task.id)}
                  className="starter-task-card"
                >
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
                      <span className={`task-diff-pill ${task.difficulty === 'Easy' ? 'task-diff-pill--easy' : 'task-diff-pill--moderate'}`}>
                        <span>{task.difficulty === 'Easy' ? '🟢' : '🟡'}</span>
                        {task.difficulty}
                      </span>
                      <span style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
                        {task.fileCount} {task.fileCount === 1 ? 'file' : 'files'}
                      </span>
                    </div>

                    <h3 style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 6 }}>
                      {task.title}
                    </h3>
                    <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', lineHeight: 1.45, marginBottom: 10 }}>
                      {task.description}
                    </p>

                    {task.relatedModuleName && (
                      <div style={{ fontSize: 11.5, color: '#79c0ff', marginBottom: 8 }}>
                        📦 <strong>Module:</strong> {task.relatedModuleName}
                      </div>
                    )}

                    <div className="task-reason-box">
                      <strong>Why this task?</strong> {task.reason}
                    </div>
                  </div>

                  <div style={{ marginTop: 16, display: 'flex', justifyContent: 'flex-end' }}>
                    <button
                      className="btn-accent"
                      onClick={e => {
                        e.stopPropagation()
                        setActiveTaskId(task.id)
                      }}
                    >
                      View Task &amp; Impact →
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* ══════════════════════════════════════════════════════════════════════ */}
      {/* MODAL: STEP DETAIL EXPERIENCE                                          */}
      {/* ══════════════════════════════════════════════════════════════════════ */}
      {activeStepId && (
        <div className="modal-backdrop" onClick={() => setActiveStepId(null)}>
          <div className="modal-dialog" onClick={e => e.stopPropagation()}>
            <button className="modal-close-btn" onClick={() => setActiveStepId(null)}>✕</button>

            {loadingStep ? (
              <div className="empty-state">
                <div style={{ fontSize: 24, marginBottom: 8 }}>📖</div>
                <div>Loading step details and grounding context...</div>
              </div>
            ) : stepDetail ? (
              <div>
                {/* Header */}
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
                  <span className="type-badge" style={{ textTransform: 'uppercase' }}>
                    {stepDetail.stepType} Step
                  </span>
                  {stepDetail.status === 'completed' && (
                    <span style={{ padding: '2px 8px', borderRadius: 6, background: 'rgba(46, 160, 67, 0.15)', color: '#3fb950', fontSize: 11, fontWeight: 700 }}>
                      Completed ✓
                    </span>
                  )}
                </div>

                <h2 style={{ fontSize: 20, fontWeight: 800, color: 'var(--text-primary)', marginBottom: 16 }}>
                  {stepDetail.title}
                </h2>

                {/* Section: What You'll Learn */}
                <div style={{ marginBottom: 16 }}>
                  <div className="card-label">What You'll Learn</div>
                  <div className="modal-subcard" style={{ whiteSpace: 'pre-line' }}>
                    {stepDetail.whatYouWillLearn}
                  </div>
                </div>

                {/* Section: Why It Matters */}
                <div style={{ marginBottom: 16 }}>
                  <div className="card-label">Why It Matters</div>
                  <div className="modal-subcard">
                    {stepDetail.whyItMatters}
                  </div>
                </div>

                {/* Section: Related Module */}
                {stepDetail.relatedModule && (
                  <div style={{ marginBottom: 16 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <div className="card-label">Related Module</div>
                      {onNavigateToArchitecture && (
                        <button
                          className="link"
                          style={{ background: 'none', border: 'none', fontSize: 12, cursor: 'pointer' }}
                          onClick={() => {
                            if (selectedRepoId && stepDetail.relatedModule?.name) {
                              setActiveStepId(null)
                              onNavigateToArchitecture(selectedRepoId, stepDetail.relatedModule.name)
                            }
                          }}
                        >
                          View in Architecture Graph →
                        </button>
                      )}
                    </div>
                    <div className="modal-subcard" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <div>
                        <strong style={{ color: 'var(--text-primary)', fontSize: 13.5 }}>{stepDetail.relatedModule.name}</strong>
                        <div style={{ fontSize: 11.5, fontFamily: 'var(--font-mono)', color: 'var(--text-muted)', marginTop: 2 }}>
                          {stepDetail.relatedModule.path || '/'}
                        </div>
                      </div>
                      <div style={{ textAlign: 'right' }}>
                        <span className="type-badge">{stepDetail.relatedModule.layer || 'Module'}</span>
                        <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>
                          {stepDetail.relatedModule.fileCount} files
                        </div>
                      </div>
                    </div>
                  </div>
                )}

                {/* Section: Relevant Files */}
                {stepDetail.relevantFiles && stepDetail.relevantFiles.length > 0 && (
                  <div style={{ marginBottom: 16 }}>
                    <div className="card-label">Relevant Files ({stepDetail.relevantFiles.length})</div>
                    <div style={{ maxHeight: 150, overflowY: 'auto', border: '1px solid var(--border-default)', borderRadius: 8, background: '#090d16' }}>
                      {stepDetail.relevantFiles.slice(0, 20).map(file => (
                        <div
                          key={file.id}
                          style={{
                            padding: '6px 12px',
                            borderBottom: '1px solid var(--border-muted)',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            fontSize: 12,
                          }}
                        >
                          <code className="code-badge" style={{ fontSize: 11 }}>{file.filePath}</code>
                          <span style={{ color: langColour(file.language), fontSize: 11 }}>
                            {file.language || file.extension} • {fmtBytes(file.fileSize)}
                          </span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Section: AI Explanation */}
                {stepDetail.aiExplanation && (
                  <div style={{ marginBottom: 20 }}>
                    <div className="card-label">AI Architecture Guidance</div>
                    <div className="modal-subcard" style={{ borderLeft: '4px solid #388bfd' }}>
                      {stepDetail.aiExplanation}
                    </div>
                  </div>
                )}

                {/* Modal Actions */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderTop: '1px solid var(--border-muted)', paddingTop: 16, flexWrap: 'wrap', gap: 10 }}>
                  {onNavigateToAssistant && (
                    <button
                      className="btn-secondary"
                      onClick={() => {
                        if (selectedRepoId) {
                          setActiveStepId(null)
                          onNavigateToAssistant(
                            selectedRepoId,
                            `Explain the learning step '${stepDetail.title}' and how the relevant files work in ${currentRepo?.name}.`
                          )
                        }
                      }}
                    >
                      💬 Ask Codebase Assistant
                    </button>
                  )}

                  <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginLeft: 'auto' }}>
                    <button className="btn-secondary" onClick={() => setActiveStepId(null)}>
                      Close
                    </button>

                    {stepDetail.status !== 'completed' ? (
                      <button
                        className="btn-primary"
                        onClick={() => handleCompleteStep(stepDetail.stepId)}
                        disabled={completingStep}
                      >
                        {completingStep ? 'Updating…' : 'Mark Complete ✓'}
                      </button>
                    ) : (
                      <span style={{ fontSize: 12, fontWeight: 700, color: '#3fb950' }}>
                        Completed on {stepDetail.completedAt ? new Date(stepDetail.completedAt).toLocaleDateString() : 'today'} ✓
                      </span>
                    )}
                  </div>
                </div>
              </div>
            ) : null}
          </div>
        </div>
      )}

      {/* ══════════════════════════════════════════════════════════════════════ */}
      {/* MODAL: STARTER TASK DETAIL & FIRST CONTRIBUTION                        */}
      {/* ══════════════════════════════════════════════════════════════════════ */}
      {activeTaskId && (
        <div className="modal-backdrop" onClick={() => setActiveTaskId(null)}>
          <div className="modal-dialog" style={{ maxWidth: 820 }} onClick={e => e.stopPropagation()}>
            <button className="modal-close-btn" onClick={() => setActiveTaskId(null)}>✕</button>

            {loadingTaskDetail ? (
              <div className="empty-state">
                <div style={{ fontSize: 24, marginBottom: 8 }}>🎯</div>
                <div>Analyzing starter task impact and safe contribution steps...</div>
              </div>
            ) : taskDetail ? (
              <div>
                {/* Header */}
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8 }}>
                  <span className={`task-diff-pill ${taskDetail.difficulty === 'Easy' ? 'task-diff-pill--easy' : 'task-diff-pill--moderate'}`}>
                    <span>{taskDetail.difficulty === 'Easy' ? '🟢' : '🟡'}</span>
                    {taskDetail.difficulty} Starter Task
                  </span>
                  {taskDetail.relatedModule && (
                    <span className="type-badge">📦 {taskDetail.relatedModule.name}</span>
                  )}
                </div>

                <h2 style={{ fontSize: 20, fontWeight: 800, color: 'var(--text-primary)', marginBottom: 8 }}>
                  {taskDetail.title}
                </h2>

                {/* 1. What the task does */}
                <div style={{ marginBottom: 14 }}>
                  <div className="card-label">1. What The Task Does</div>
                  <p style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5 }}>
                    {taskDetail.description}
                  </p>
                </div>

                {/* 2. Why recommended */}
                <div style={{ marginBottom: 14 }}>
                  <div className="card-label">2. Why It Is Recommended</div>
                  <div className="modal-subcard" style={{ borderLeft: '4px solid #3fb950' }}>
                    {taskDetail.reasonForRecommendation}
                  </div>
                </div>

                {/* 3. Relevant files */}
                <div style={{ marginBottom: 14 }}>
                  <div className="card-label">3. Relevant Files ({taskDetail.relevantFiles.length})</div>
                  <div style={{ maxHeight: 120, overflowY: 'auto', border: '1px solid var(--border-default)', borderRadius: 8, background: '#090d16' }}>
                    {taskDetail.relevantFiles.map(file => (
                      <div
                        key={file.id}
                        style={{
                          padding: '6px 12px',
                          borderBottom: '1px solid var(--border-muted)',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'space-between',
                          fontSize: 12,
                        }}
                      >
                        <code className="code-badge" style={{ fontSize: 11 }}>{file.filePath}</code>
                        <span style={{ color: langColour(file.language), fontSize: 11 }}>{file.language}</span>
                      </div>
                    ))}
                  </div>
                </div>

                {/* 4. Suggested first step */}
                <div style={{ marginBottom: 14 }}>
                  <div className="card-label">4. Suggested First Step</div>
                  <div className="modal-subcard" style={{ borderLeft: '4px solid #388bfd' }}>
                    {taskDetail.suggestedFirstStep}
                  </div>
                </div>

                {/* 5. Existing Code Pattern */}
                <div style={{ marginBottom: 16 }}>
                  <div className="card-label">5. Existing Code Pattern Reference</div>
                  <div className="modal-subcard">
                    {taskDetail.existingPattern}
                  </div>
                </div>

                {/* 6. Change Impact Analysis */}
                <div style={{ marginBottom: 20 }}>
                  <div className="card-label">6. Change Impact Analysis (Grounded in Repository Intelligence)</div>
                  <div className="table-container">
                    <table className="table" style={{ fontSize: 12 }}>
                      <thead>
                        <tr>
                          <th>File</th>
                          <th>Module</th>
                          <th>Impact Level</th>
                          <th>Rationale</th>
                        </tr>
                      </thead>
                      <tbody>
                        {taskDetail.changeImpact.map((item, idx) => (
                          <tr key={idx}>
                            <td><code className="code-badge" style={{ fontSize: 11 }}>{item.filePath}</code></td>
                            <td style={{ color: 'var(--text-secondary)' }}>{item.moduleName || '—'}</td>
                            <td><span className={`impact-badge impact-badge--${item.impact}`}>{item.impact}</span></td>
                            <td style={{ color: 'var(--text-secondary)', fontSize: 11.5 }}>{item.reason}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>

                {/* Modal Actions */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderTop: '1px solid var(--border-muted)', paddingTop: 16, flexWrap: 'wrap', gap: 10 }}>
                  <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
                    {onNavigateToAssistant && (
                      <>
                        <button
                          className="btn-accent"
                          onClick={() => {
                            if (selectedRepoId) {
                              setActiveTaskId(null)
                              onNavigateToAssistant(
                                selectedRepoId,
                                `How should I implement the starter task '${taskDetail.title}' in ${currentRepo?.name}? Provide safe code steps.`
                              )
                            }
                          }}
                        >
                          💬 Ask Codebase Assistant
                        </button>

                        <button
                          className="btn-secondary"
                          onClick={() => {
                            if (selectedRepoId) {
                              setActiveTaskId(null)
                              onNavigateToAssistant(
                                selectedRepoId,
                                `What is the architectural change impact of completing '${taskDetail.title}' across ${currentRepo?.name}?`,
                                'change-impact'
                              )
                            }
                          }}
                        >
                          ⚡ Analyze Impact in Assistant
                        </button>
                      </>
                    )}
                  </div>

                  <button className="btn-secondary" onClick={() => setActiveTaskId(null)}>
                    Close
                  </button>
                </div>
              </div>
            ) : null}
          </div>
        </div>
      )}
    </div>
  )
}
