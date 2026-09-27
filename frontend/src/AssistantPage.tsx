import { useState, useEffect, useRef } from 'react'

// ── Types ──────────────────────────────────────────────────────────────────
interface Repository {
  id: number
  name: string
  gitUrl: string
  primaryLanguage: string
}

interface FileReference {
  filePath: string
  language: string
  reason: string
}

interface ModuleReference {
  moduleId: number
  moduleName: string
  modulePath: string
  moduleType: string
}

interface ChangeImpactItem {
  filePath: string
  moduleName: string
  impact: string
  reason: string
}

interface ContextSummary {
  filesExamined: number
  modulesExamined: number
  dependenciesExamined: number
  sourceCodeRead: boolean
  repositoryName: string
}

interface AssistantAnswer {
  answer: string
  questionType: string
  references: FileReference[]
  modules: ModuleReference[]
  changeImpact: ChangeImpactItem[]
  context: ContextSummary
  sessionId: string
}

interface ConversationItem {
  id: number
  question: string
  answer: string
  questionType: string
  createdAt: string
  references?: FileReference[]
  modules?: ModuleReference[]
  changeImpact?: ChangeImpactItem[]
  context?: ContextSummary
}

interface SuggestedQuestions {
  questions: string[]
}

type AskState = 'idle' | 'asking' | 'done' | 'error'

const LANG_COLOURS: Record<string, string> = {
  'C#': '#178600', TypeScript: '#3178c6', JavaScript: '#f1e05a',
  Python: '#3572A5', Java: '#b07219', HTML: '#e34c26',
}
const langColour = (lang: string) => LANG_COLOURS[lang] ?? '#8b949e'

// ══════════════════════════════════════════════════════════════════════════
interface AssistantPageProps {
  initialRepoId?: number | null
  initialQuestion?: string | null
  initialQuestionType?: 'ask' | 'change-impact'
  aiProvider?: string
}

export default function AssistantPage({
  initialRepoId,
  initialQuestion,
  initialQuestionType,
  aiProvider: propAiProvider,
}: AssistantPageProps = {}) {
  const [repositories, setRepositories]     = useState<Repository[]>([])
  const [selectedRepo, setSelectedRepo]     = useState<number | null>(initialRepoId ?? null)
  const [questionType, setQuestionType]     = useState<'ask' | 'change-impact'>(initialQuestionType ?? 'ask')
  const [question, setQuestion]             = useState(initialQuestion ?? '')
  const [askState, setAskState]             = useState<AskState>('idle')
  const [error, setError]                   = useState('')
  const [conversation, setConversation]     = useState<ConversationItem[]>([])
  const [suggestions, setSuggestions]       = useState<string[]>([])
  const [sessionId, setSessionId]           = useState<string | undefined>(undefined)
  const [selectedFile, setSelectedFile]     = useState<FileReference | null>(null)
  const [aiProvider, setAiProvider]         = useState<string>(propAiProvider || 'Repository-grounded mode')
  const bottomRef = useRef<HTMLDivElement>(null)

  // ── Sync initial props when passed ───────────────────────────────────────
  useEffect(() => {
    if (initialRepoId) setSelectedRepo(initialRepoId)
  }, [initialRepoId])

  useEffect(() => {
    if (initialQuestion !== undefined && initialQuestion !== null) setQuestion(initialQuestion)
  }, [initialQuestion])

  useEffect(() => {
    if (initialQuestionType) setQuestionType(initialQuestionType)
  }, [initialQuestionType])

  // ── Load repositories & health ──────────────────────────────────────────
  useEffect(() => {
    fetch('/api/health')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (data?.aiProvider) setAiProvider(data.aiProvider)
      })
      .catch(() => {})

    fetch('/api/repositories')
      .then(r => r.ok ? r.json() : [])
      .then((repos: Repository[]) => {
        setRepositories(repos)
        if (repos.length > 0) {
          setSelectedRepo(prev => prev && repos.some(r => r.id === prev) ? prev : repos[0].id)
        }
      })
      .catch(() => {})
  }, [initialRepoId])

  // ── Load suggestions when repo changes ──────────────────────────────────
  useEffect(() => {
    if (!selectedRepo) return
    setConversation([])
    setSessionId(undefined)
    setSuggestions([])
    fetch(`/api/repositories/${selectedRepo}/assistant/suggestions`)
      .then(r => r.ok ? r.json() : { questions: [] })
      .then((s: SuggestedQuestions) => setSuggestions(s.questions))
      .catch(() => {})
  }, [selectedRepo])

  // ── Auto-scroll ──────────────────────────────────────────────────────────
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [conversation, askState])

  // ── Ask handler ──────────────────────────────────────────────────────────
  const ask = async (q?: string) => {
    const finalQuestion = (q ?? question).trim()
    if (!finalQuestion || !selectedRepo || askState === 'asking') return

    setQuestion(q ? '' : '')
    setAskState('asking')
    setError('')

    try {
      const res = await fetch(`/api/repositories/${selectedRepo}/assistant/ask`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question: finalQuestion, questionType, sessionId }),
      })
      const body = await res.json()
      if (!res.ok) { setAskState('error'); setError(body.error ?? 'Request failed'); return }

      const answer = body as AssistantAnswer
      setSessionId(answer.sessionId)

      setConversation(prev => [...prev, {
        id: Date.now(),
        question: finalQuestion,
        answer: answer.answer,
        questionType: answer.questionType,
        createdAt: new Date().toISOString(),
        references: answer.references,
        modules: answer.modules,
        changeImpact: answer.changeImpact,
        context: answer.context,
      }])

      setAskState('done')
    } catch {
      setAskState('error')
      setError('Could not reach the backend API.')
    }
  }

  const clearChat = () => {
    setConversation([])
    setSessionId(undefined)
    setError('')
    setSelectedFile(null)
  }

  const currentRepoObj = repositories.find(r => r.id === selectedRepo)

  // ── Render ─────────────────────────────────────────────────────────────
  return (
    <div className="asst-page">
      {/* ── Header ── */}
      <header className="page-header">
        <div>
          <h1 className="page-title">Codebase Assistant</h1>
          <p className="page-subtitle">Real code context retrieval, architecture questions, and change-impact analysis</p>
        </div>
      </header>

      <div className="asst-layout">
        {/* ── Main Conversation Area ── */}
        <div className="asst-main">
          {/* Controls Bar */}
          <div className="asst-controls">
            <div className="asst-control-group">
              <label className="asst-label">Repository</label>
              <select
                className="arch-select"
                value={selectedRepo ?? ''}
                onChange={e => setSelectedRepo(Number(e.target.value))}
              >
                {repositories.length === 0
                  ? <option>No repositories analyzed yet</option>
                  : repositories.map(r => <option key={r.id} value={r.id}>{r.name} ({r.primaryLanguage || 'Code'})</option>)}
              </select>
            </div>

            <div className="asst-control-group">
              <label className="asst-label">Query Mode</label>
              <div className="asst-mode-tabs">
                <button
                  className={`asst-mode-tab ${questionType === 'ask' ? 'asst-mode-tab--active' : ''}`}
                  onClick={() => setQuestionType('ask')}
                >
                  💬 Codebase Q&amp;A
                </button>
                <button
                  className={`asst-mode-tab ${questionType === 'change-impact' ? 'asst-mode-tab--active' : ''}`}
                  onClick={() => setQuestionType('change-impact')}
                >
                  ⚡ Change Impact
                </button>
              </div>
            </div>

            {/* AI Provider Status Badge */}
            <div className="asst-control-group" style={{ marginLeft: 'auto' }}>
              <label className="asst-label">Intelligence Engine</label>
              <div
                className="header-ai-pill"
                style={{ padding: '5px 10px', fontSize: 11.5 }}
                title="AI Grounding Engine Status"
              >
                <span>{aiProvider.includes('watsonx') ? '⚡' : '🧠'}</span>
                <span>{aiProvider}</span>
              </div>
            </div>

            {conversation.length > 0 && (
              <button
                className="arch-clear-btn"
                style={{ alignSelf: 'flex-end', padding: '5px 10px' }}
                onClick={clearChat}
              >
                Clear Chat
              </button>
            )}
          </div>

          {/* Conversation Thread */}
          <div className="asst-thread">
            {conversation.length === 0 && askState !== 'asking' && (
              <div className="asst-empty" style={{ maxWidth: 540, margin: '40px auto', textAlign: 'center' }}>
                <div style={{ fontSize: 32, marginBottom: 12 }}>
                  {questionType === 'change-impact' ? '⚡' : '◆'}
                </div>
                <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 6 }}>
                  {questionType === 'change-impact'
                    ? 'Simulate Code Modification Blast Radius'
                    : `Ask Anything About ${currentRepoObj?.name || 'Your Codebase'}`}
                </div>
                <p style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5, marginBottom: 18 }}>
                  {questionType === 'change-impact'
                    ? 'Describe an addition or refactor (e.g. "I want to add user authentication to the API") to calculate direct and downstream file impacts.'
                    : 'Get precise explanations with verified source code citations, related architectural modules, and file references.'}
                </p>

                {/* Inline Suggested Questions Chips */}
                {suggestions.length > 0 && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 8, alignItems: 'stretch' }}>
                    <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--text-muted)' }}>
                      Suggested Inquiries for {currentRepoObj?.name}
                    </div>
                    {suggestions.slice(0, 3).map((q, i) => (
                      <button
                        key={i}
                        className="btn-secondary"
                        style={{ textAlign: 'left', justifyContent: 'flex-start', padding: '8px 12px', fontSize: 12 }}
                        onClick={() => ask(q)}
                      >
                        <span style={{ color: '#58a6ff' }}>→</span>
                        <span>{q}</span>
                      </button>
                    ))}
                  </div>
                )}
              </div>
            )}

            {conversation.map((item, idx) => (
              <div key={item.id} className="asst-exchange">
                {/* Question bubble */}
                <div className="asst-question-row">
                  <div className="asst-question-bubble">
                    <span className="asst-q-type">
                      {item.questionType === 'change-impact' ? '⚡ Change Impact Query' : '💬 Question'}
                    </span>
                    {item.question}
                  </div>
                </div>

                {/* Answer card */}
                <div className="asst-answer-card">
                  {/* Grounding Context Metadata */}
                  {item.context && (
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10, fontSize: 11, color: 'var(--text-muted)', borderBottom: '1px solid var(--border-muted)', paddingBottom: 8 }}>
                      <span>🔍 Grounded in: <strong>{item.context.filesExamined} files</strong>, <strong>{item.context.modulesExamined} modules</strong></span>
                      {item.context.sourceCodeRead && <span>• Source code verified ✓</span>}
                    </div>
                  )}

                  <MarkdownAnswer text={item.answer} />

                  {/* Change impact table */}
                  {item.changeImpact && item.changeImpact.length > 0 && (
                    <div className="asst-impact-section">
                      <div className="asst-section-title">Change Impact Analysis</div>
                      <div className="table-container">
                        <table className="table" style={{ fontSize: 12 }}>
                          <thead>
                            <tr>
                              <th>File</th>
                              <th>Module</th>
                              <th>Impact Level</th>
                              <th>Reason &amp; Downstream Effect</th>
                            </tr>
                          </thead>
                          <tbody>
                            {item.changeImpact.map((ci, i) => (
                              <tr key={i}>
                                <td><code className="code-badge" style={{ fontSize: 11 }}>{ci.filePath}</code></td>
                                <td style={{ color: 'var(--text-secondary)' }}>{ci.moduleName || '—'}</td>
                                <td><span className={`impact-badge impact-badge--${ci.impact}`}>{ci.impact}</span></td>
                                <td style={{ color: 'var(--text-secondary)', fontSize: 11.5 }}>{ci.reason}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {/* File references */}
                  {item.references && item.references.length > 0 && (
                    <div className="asst-refs">
                      <div className="asst-section-title">Cited File References ({item.references.length})</div>
                      <div className="asst-ref-list">
                        {item.references.map((ref, i) => (
                          <button
                            key={i}
                            className="asst-ref-item"
                            onClick={() => setSelectedFile(selectedFile?.filePath === ref.filePath ? null : ref)}
                          >
                            <span className="arch-file-dot" style={{ background: langColour(ref.language) }} />
                            <code className="asst-ref-path">{ref.filePath}</code>
                            {ref.language && <span className="asst-ref-lang">{ref.language}</span>}
                            <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>{selectedFile?.filePath === ref.filePath ? '▲' : '▼'}</span>
                          </button>
                        ))}
                      </div>

                      {/* File detail drawer */}
                      {selectedFile && item.references.some(r => r.filePath === selectedFile.filePath) && (
                        <div className="asst-file-detail">
                          <div className="asst-file-detail-header">
                            <span className="arch-file-dot" style={{ background: langColour(selectedFile.language) }} />
                            <strong>{selectedFile.filePath}</strong>
                            <button className="arch-detail-close" onClick={() => setSelectedFile(null)}>×</button>
                          </div>
                          <div className="arch-detail-grid">
                            <span className="arch-detail-key">Language</span>
                            <span>{selectedFile.language || '—'}</span>
                            <span className="arch-detail-key">Citation Purpose</span>
                            <span style={{ color: 'var(--text-secondary)' }}>{selectedFile.reason}</span>
                          </div>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Module references */}
                  {item.modules && item.modules.length > 0 && (
                    <div className="asst-refs">
                      <div className="asst-section-title">Related Structural Modules ({item.modules.length})</div>
                      <div className="asst-ref-list">
                        {item.modules.map((m, i) => (
                          <div key={i} className="asst-mod-item" style={{ background: 'rgba(255, 255, 255, 0.02)', padding: '6px 10px', borderRadius: 6, border: '1px solid var(--border-default)' }}>
                            <span className="type-badge">{m.moduleType}</span>
                            <strong style={{ color: 'var(--text-primary)', fontSize: 12.5 }}>{m.moduleName}</strong>
                            <code className="code-badge" style={{ fontSize: 10.5, marginLeft: 'auto' }}>{m.modulePath}</code>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {/* Quick Mentor Follow-up Actions for the latest message */}
                  {idx === conversation.length - 1 && (
                    <div className="asst-mentor-actions">
                      <div className="asst-mentor-actions-title">Mentor Next Steps &amp; Practice:</div>
                      <div className="asst-mentor-chips">
                        {getSuggestedChips(item.answer).map((actionText, aIdx) => (
                          <button
                            key={aIdx}
                            className="asst-action-chip"
                            onClick={() => ask(actionText)}
                            disabled={askState === 'asking'}
                          >
                            <span>→</span> {actionText}
                          </button>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              </div>
            ))}

            {/* Loading indicator */}
            {askState === 'asking' && (
              <div className="asst-thinking">
                <div className="asst-thinking-dots"><span /><span /><span /></div>
                <span>Analyzing codebase context &amp; retrieving real repository code...</span>
              </div>
            )}

            {error && <div className="alert alert--error" style={{ margin: '8px 0' }}>{error}</div>}
            <div ref={bottomRef} />
          </div>

          {/* Input Bar */}
          <div className="asst-input-bar">
            <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
              <textarea
                className="asst-textarea"
                placeholder={questionType === 'change-impact'
                  ? 'Describe the architectural or code change you want to make…'
                  : 'Ask anything about this codebase, architecture, or workflows…'}
                value={question}
                onChange={e => setQuestion(e.target.value)}
                onKeyDown={e => {
                  if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault()
                    ask()
                  }
                }}
                rows={2}
                disabled={askState === 'asking'}
              />
              <div style={{ fontSize: 11, color: 'var(--text-muted)', padding: '2px 8px 0' }}>
                Press <strong>Enter ↵</strong> to send • <strong>Shift+Enter</strong> for new line
              </div>
            </div>

            <button
              className="asst-send-btn"
              onClick={() => ask()}
              disabled={askState === 'asking' || !question.trim() || !selectedRepo}
            >
              {askState === 'asking' ? 'Analyzing…' : 'Ask'}
            </button>
          </div>
        </div>

        {/* ── Right Sidebar: Suggested Questions & History ── */}
        <div className="asst-sidebar">
          {suggestions.length > 0 && (
            <div className="asst-suggestions">
              <div className="arch-panel-title">
                <span>Suggested Questions</span>
                <span className="arch-badge">{suggestions.length}</span>
              </div>
              <div className="asst-suggestion-list">
                {suggestions.map((q, i) => (
                  <button
                    key={i}
                    className="asst-suggestion-btn"
                    onClick={() => ask(q)}
                    disabled={askState === 'asking'}
                  >
                    {q}
                  </button>
                ))}
              </div>
            </div>
          )}

          {conversation.length > 0 && (
            <div className="asst-history-summary">
              <div className="arch-panel-title">
                <span>Current Session</span>
                <span className="arch-badge">{conversation.length}</span>
              </div>
              <div className="asst-history-list">
                {[...conversation].reverse().slice(0, 8).map(item => (
                  <div key={item.id} className="asst-history-item" title={item.question}>
                    <span className="asst-history-q">
                      {item.question.length > 55 ? item.question.slice(0, 55) + '…' : item.question}
                    </span>
                    <span className="asst-history-type" title={item.questionType}>
                      {item.questionType === 'change-impact' ? '⚡' : '💬'}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ── Markdown Answer Renderer ──────────────────────────────────────────────
interface ParsedBlock {
  type: 'h2' | 'h3' | 'h4' | 'bullet' | 'numbered' | 'quote' | 'code' | 'flow' | 'para' | 'spacer'
  content: string
  num?: string
  lang?: string
}

function MarkdownAnswer({ text }: { text: string }) {
  const lines = text.split('\n')
  const blocks: ParsedBlock[] = []

  let inCode = false
  let codeBuffer: string[] = []
  let codeLang = ''

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]

    if (line.startsWith('```')) {
      if (inCode) {
        // Closing code block
        const codeText = codeBuffer.join('\n')
        const isFlow = codeText.includes('↓') || codeText.includes('->') || codeText.includes('-->')
        blocks.push({
          type: isFlow ? 'flow' : 'code',
          content: codeText,
          lang: codeLang,
        })
        codeBuffer = []
        inCode = false
      } else {
        // Opening code block
        inCode = true
        codeLang = line.slice(3).trim()
      }
      continue
    }

    if (inCode) {
      codeBuffer.push(line)
      continue
    }

    if (line.startsWith('### ')) {
      blocks.push({ type: 'h4', content: line.slice(4).trim() })
    } else if (line.startsWith('## ')) {
      blocks.push({ type: 'h3', content: line.slice(3).trim() })
    } else if (line.startsWith('# ')) {
      blocks.push({ type: 'h2', content: line.slice(2).trim() })
    } else if (line.startsWith('> ')) {
      blocks.push({ type: 'quote', content: line.slice(2).trim() })
    } else if (line.startsWith('- ') || line.startsWith('* ')) {
      blocks.push({ type: 'bullet', content: line.slice(2).trim() })
    } else {
      const numMatch = line.match(/^(\d+)\.\s+(.*)$/)
      if (numMatch) {
        blocks.push({ type: 'numbered', num: numMatch[1], content: numMatch[2] })
      } else if (line.trim() === '') {
        blocks.push({ type: 'spacer', content: '' })
      } else {
        blocks.push({ type: 'para', content: line })
      }
    }
  }

  // Handle unclosed code block if any
  if (inCode && codeBuffer.length > 0) {
    const codeText = codeBuffer.join('\n')
    const isFlow = codeText.includes('↓') || codeText.includes('->')
    blocks.push({
      type: isFlow ? 'flow' : 'code',
      content: codeText,
      lang: codeLang,
    })
  }

  return (
    <div className="asst-answer-text">
      {blocks.map((b, idx) => {
        switch (b.type) {
          case 'h2':
            return <h2 key={idx} className="asst-h2">{renderInline(b.content)}</h2>
          case 'h3':
            return <h3 key={idx} className="asst-h3">{renderInline(b.content)}</h3>
          case 'h4':
            return <h4 key={idx} className="asst-h4">{renderInline(b.content)}</h4>
          case 'bullet':
            return <div key={idx} className="asst-bullet">{renderInline(b.content)}</div>
          case 'numbered':
            return (
              <div key={idx} className="asst-num-item">
                <span className="asst-num-badge">{b.num}</span>
                <span style={{ flex: 1 }}>{renderInline(b.content)}</span>
              </div>
            )
          case 'quote':
            return <div key={idx} className="asst-quote">{renderInline(b.content)}</div>
          case 'flow':
            return (
              <pre key={idx} className="asst-flow-box">
                {b.content}
              </pre>
            )
          case 'code':
            return (
              <pre key={idx} className="asst-code-block">
                <code>{b.content}</code>
              </pre>
            )
          case 'spacer':
            return <div key={idx} className="asst-spacer" />
          case 'para':
          default:
            return <div key={idx} className="asst-para">{renderInline(b.content)}</div>
        }
      })}
    </div>
  )
}

function getSuggestedChips(answerText: string): string[] {
  const lower = answerText.toLowerCase()
  if (lower.includes('roadmap') || lower.includes('level 1') || lower.includes('phase 1')) {
    return [
      'Start Level 1',
      'Explain the login flow step by step',
      'What authentication features are actually implemented in this repository?',
      'Quiz me on authentication',
      'Continue',
    ]
  }
  if (lower.includes('quiz') || lower.includes('check your understanding') || lower.includes('question 1')) {
    return [
      'Show the correct answers with code evidence',
      'Give me hints on question 1',
      'What should I learn next?',
      'Continue',
    ]
  }
  if (lower.includes('level 1') || lower.includes('lesson 1')) {
    return [
      'Continue to next lesson',
      'Quiz me on authentication',
      'Show me the frontend-to-backend authentication flow',
      'What should I learn next?',
    ]
  }
  return [
    'Start Level 1',
    'Explain the login flow step by step',
    'What authentication features are actually implemented in this repository?',
    'What authentication features are missing?',
    'Quiz me on authentication',
    'What should I learn next?',
    'Continue',
  ]
}

function renderInline(text: string) {
  // Bold **text**, inline `code`, and status badges
  const parts = text.split(/(\*\*[^*]+\*\*|`[^`]+`|\[IMPLEMENTED\]|\[PARTIALLY IMPLEMENTED\]|\[NOT IMPLEMENTED — NEXT CONCEPT\]|\[NOT IMPLEMENTED - NEXT CONCEPT\]|\[NOT IMPLEMENTED\]|\[MISSING\])/g)
  return parts.map((part, i) => {
    if (part.startsWith('**') && part.endsWith('**'))
      return <strong key={i} style={{ color: 'var(--text-primary)' }}>{part.slice(2, -2)}</strong>
    if (part.startsWith('`') && part.endsWith('`'))
      return <code key={i} className="code-badge" style={{ fontSize: 12 }}>{part.slice(1, -1)}</code>
    if (part === '[IMPLEMENTED]')
      return <span key={i} className="status-badge status-badge--implemented">✓ IMPLEMENTED</span>
    if (part === '[PARTIALLY IMPLEMENTED]')
      return <span key={i} className="status-badge status-badge--partial">◐ PARTIALLY IMPLEMENTED</span>
    if (part === '[NOT IMPLEMENTED — NEXT CONCEPT]' || part === '[NOT IMPLEMENTED - NEXT CONCEPT]')
      return <span key={i} className="status-badge status-badge--missing">○ NEXT CONCEPT</span>
    if (part === '[NOT IMPLEMENTED]' || part === '[MISSING]')
      return <span key={i} className="status-badge status-badge--missing">✕ NOT IMPLEMENTED</span>
    return <span key={i}>{part}</span>
  })
}
