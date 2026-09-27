import { useState, useCallback, useEffect } from 'react'
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  useNodesState,
  useEdgesState,
  addEdge,
  type Node,
  type Edge,
  type Connection,
  MarkerType,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'

// ── Types ──────────────────────────────────────────────────────────────────
interface ArchNode {
  id: string; type: string; name: string; path: string
  description: string; moduleType: string; layer: string
  fileCount: number; outgoingDeps: number; incomingDeps: number
}

interface ArchEdge {
  id: string; source: string; target: string
  relationshipType: string; weight: number
}

interface ArchGraph {
  repositoryId: number; repositoryName: string
  nodes: ArchNode[]; edges: ArchEdge[]
}

interface RepoFile {
  id: number; filePath: string; fileName: string
  extension: string; language: string; fileSize: number; isDirectory: boolean
}

interface ModuleDep { moduleId: number; moduleName: string; modulePath: string; dependencyCount: number }

interface ModuleDetail {
  id: number; name: string; path: string; moduleType: string
  description: string; layer: string; fileCount: number
  files: RepoFile[]
  outgoingDependencies: ModuleDep[]
  incomingDependencies: ModuleDep[]
  relatedModules: { id: number; name: string; path: string; moduleType: string }[]
}

interface FlowStep { order: number; nodeId: string; name: string; path: string; nodeType: string }
interface FlowResult { pathFound: boolean; from: string; to: string; message: string; steps: FlowStep[] }

interface Repository { id: number; name: string; gitUrl: string; primaryLanguage: string }

// ── Layer colours (dark developer theme) ──────────────────────────────────
const LAYER_COLOURS: Record<string, { bg: string; border: string; text: string }> = {
  'Presentation':   { bg: 'rgba(59, 130, 246, 0.14)', border: '#3b82f6', text: '#93c5fd' },
  'API':            { bg: 'rgba(34, 197, 94, 0.14)', border: '#22c55e', text: '#86efac' },
  'Business Logic': { bg: 'rgba(234, 179, 8, 0.14)', border: '#eab308', text: '#fde047' },
  'Data Access':    { bg: 'rgba(244, 63, 94, 0.14)', border: '#f43f5e', text: '#fda4af' },
  'Domain':         { bg: 'rgba(168, 85, 247, 0.14)', border: '#a855f7', text: '#d8b4fe' },
  'Infrastructure': { bg: 'rgba(148, 163, 184, 0.14)', border: '#94a3b8', text: '#cbd5e1' },
  'Backend':        { bg: 'rgba(16, 185, 129, 0.14)', border: '#10b981', text: '#6ee7b7' },
  'Shared':         { bg: 'rgba(217, 119, 6, 0.14)', border: '#d97706', text: '#fcd34d' },
  'Tests':          { bg: 'rgba(2, 132, 199, 0.14)', border: '#0284c7', text: '#7dd3fc' },
  'Documentation':  { bg: 'rgba(16, 185, 129, 0.14)', border: '#10b981', text: '#6ee7b7' },
}
const getLayerStyle = (layer: string) =>
  LAYER_COLOURS[layer] ?? { bg: 'rgba(148, 163, 184, 0.12)', border: '#475569', text: '#e2e8f0' }

const LANG_COLOURS: Record<string, string> = {
  'C#': '#178600', TypeScript: '#3178c6', JavaScript: '#f1e05a',
  Python: '#3572A5', Java: '#b07219', HTML: '#e34c26',
  Go: '#00ADD8', Rust: '#dea584', Ruby: '#CC342D',
}
const langColour = (lang: string) => LANG_COLOURS[lang] ?? '#8b949e'
const fmt = (n: number) => n.toLocaleString()
const fmtBytes = (b: number) => b < 1024 ? `${b} B` : b < 1048576 ? `${(b/1024).toFixed(1)} KB` : `${(b/1048576).toFixed(1)} MB`

// ── Layout: simple layered column arrangement ──────────────────────────────
const LAYER_ORDER = [
  'Presentation', 'API', 'Backend', 'Business Logic',
  'Data Access', 'Domain', 'Infrastructure', 'Shared', 'Library',
  'Root', 'Tests', 'Documentation',
]

function computeLayout(archNodes: ArchNode[]): Node[] {
  // Group by layer
  const groups: Record<string, ArchNode[]> = {}
  for (const n of archNodes) {
    const layer = n.layer || 'Unknown'
    ;(groups[layer] = groups[layer] ?? []).push(n)
  }

  const layerKeys = [
    ...LAYER_ORDER.filter(l => groups[l]),
    ...Object.keys(groups).filter(l => !LAYER_ORDER.includes(l)),
  ]

  const NODE_W = 200, NODE_H = 80, GAP_X = 60, GAP_Y = 140
  const nodes: Node[] = []

  layerKeys.forEach((layer, colIdx) => {
    const group = groups[layer]
    group.forEach((n, rowIdx) => {
      const style = getLayerStyle(layer)
      nodes.push({
        id: n.id,
        type: 'default',
        position: { x: colIdx * (NODE_W + GAP_X), y: rowIdx * (NODE_H + GAP_Y) },
        data: {
          label: (
            <div className="arch-node-inner" style={{ borderColor: style.border }}>
              <div className="arch-node-name" style={{ color: style.text }}>{n.name}</div>
              <div className="arch-node-layer" style={{ color: style.text, opacity: 0.75 }}>{n.layer}</div>
              <div className="arch-node-stats">
                <span>{n.fileCount} files</span>
                {n.outgoingDeps > 0 && <span>↑{n.outgoingDeps}</span>}
                {n.incomingDeps > 0 && <span>↓{n.incomingDeps}</span>}
              </div>
            </div>
          ),
          archNode: n,
        },
        style: {
          background: style.bg,
          border: `1.5px solid ${style.border}`,
          borderRadius: 8,
          width: NODE_W,
          padding: 0,
          cursor: 'pointer',
          boxShadow: '0 4px 12px rgba(0, 0, 0, 0.4)',
        },
      })
    })
  })
  return nodes
}

function buildEdges(archEdges: ArchEdge[], highlightedIds: Set<string>): Edge[] {
  return archEdges.map(e => ({
    id: e.id,
    source: e.source,
    target: e.target,
    type: 'smoothstep',
    animated: highlightedIds.size > 0 && (highlightedIds.has(e.source) || highlightedIds.has(e.target)),
    markerEnd: {
      type: MarkerType.ArrowClosed,
      width: 14,
      height: 14,
      color: highlightedIds.size > 0 && highlightedIds.has(e.source) && highlightedIds.has(e.target) ? '#58a6ff' : '#484f58',
    },
    style: {
      stroke: highlightedIds.size > 0
        ? (highlightedIds.has(e.source) && highlightedIds.has(e.target) ? '#58a6ff' : '#1f2937')
        : '#38444d',
      strokeWidth: e.weight > 1 ? Math.min(e.weight + 1, 4) : 1.5,
    },
    label: e.weight > 1 ? `${e.weight}` : undefined,
  }))
}

// ══════════════════════════════════════════════════════════════════════════
interface ArchitecturePageProps {
  initialRepoId?: number | null
  highlightModuleName?: string | null
}

export default function ArchitecturePage({
  initialRepoId,
  highlightModuleName,
}: ArchitecturePageProps = {}) {
  const [repositories, setRepositories]     = useState<Repository[]>([])
  const [selectedRepo, setSelectedRepo]     = useState<number | null>(initialRepoId ?? null)
  const [graph, setGraph]                   = useState<ArchGraph | null>(null)
  const [loading, setLoading]               = useState(false)
  const [error, setError]                   = useState('')

  const [selectedModule, setSelectedModule] = useState<ArchNode | null>(null)
  const [moduleDetail, setModuleDetail]     = useState<ModuleDetail | null>(null)
  const [detailLoading, setDetailLoading]   = useState(false)

  const [searchQuery, setSearchQuery]       = useState(highlightModuleName ?? '')
  const [highlighted, setHighlighted]       = useState<Set<string>>(new Set())

  // Flow tool
  const [flowFrom, setFlowFrom]             = useState('')
  const [flowTo, setFlowTo]                 = useState('')
  const [flowResult, setFlowResult]         = useState<FlowResult | null>(null)
  const [flowLoading, setFlowLoading]       = useState(false)

  const [nodes, setNodes, onNodesChange]    = useNodesState<Node>([])
  const [edges, setEdges, onEdgesChange]    = useEdgesState<Edge>([])
  const onConnect = useCallback((c: Connection) => setEdges(e => addEdge(c, e)), [setEdges])

  // ── Sync props ───────────────────────────────────────────────────────────
  useEffect(() => {
    if (initialRepoId) setSelectedRepo(initialRepoId)
  }, [initialRepoId])

  useEffect(() => {
    if (highlightModuleName) setSearchQuery(highlightModuleName)
  }, [highlightModuleName])

  // ── Load repositories ───────────────────────────────────────────────────
  useEffect(() => {
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

  // ── Load graph when repo changes ────────────────────────────────────────
  useEffect(() => {
    if (!selectedRepo) return
    setLoading(true)
    setError('')
    setGraph(null)
    setSelectedModule(null)
    setModuleDetail(null)
    setHighlighted(new Set())

    fetch(`/api/repositories/${selectedRepo}/architecture`)
      .then(r => r.ok ? r.json() : Promise.reject(r))
      .then((g: ArchGraph) => {
        setGraph(g)
        const rfNodes = computeLayout(g.nodes)
        const rfEdges = buildEdges(g.edges, new Set())
        setNodes(rfNodes)
        setEdges(rfEdges)
      })
      .catch(() => setError('Failed to load architecture. Make sure this repository has been analyzed.'))
      .finally(() => setLoading(false))
  }, [selectedRepo, setNodes, setEdges])

  // ── Search handler ──────────────────────────────────────────────────────
  useEffect(() => {
    if (!graph) return
    const q = searchQuery.trim().toLowerCase()
    if (!q) {
      setHighlighted(new Set())
      setEdges(buildEdges(graph.edges, new Set()))
      return
    }
    const matches = new Set<string>(
      graph.nodes
        .filter(n =>
          n.name.toLowerCase().includes(q) ||
          n.path.toLowerCase().includes(q) ||
          n.layer.toLowerCase().includes(q) ||
          n.moduleType.toLowerCase().includes(q)
        )
        .map(n => n.id)
    )
    setHighlighted(matches)
    setEdges(buildEdges(graph.edges, matches))
  }, [searchQuery, graph, setEdges])

  // ── Node click ──────────────────────────────────────────────────────────
  const onNodeClick = useCallback((_: React.MouseEvent, node: Node) => {
    const archNode = (node.data as { archNode: ArchNode }).archNode
    setSelectedModule(archNode)
    setDetailLoading(true)
    setModuleDetail(null)

    const moduleId = parseInt(archNode.id.replace('module-', ''))
    fetch(`/api/repositories/${selectedRepo}/modules/${moduleId}`)
      .then(r => r.ok ? r.json() : null)
      .then((d: ModuleDetail | null) => setModuleDetail(d))
      .catch(() => setModuleDetail(null))
      .finally(() => setDetailLoading(false))

    // Highlight node and its neighbours
    if (graph) {
      const related = new Set<string>([archNode.id])
      for (const e of graph.edges) {
        if (e.source === archNode.id) related.add(e.target)
        if (e.target === archNode.id) related.add(e.source)
      }
      setHighlighted(related)
      setEdges(buildEdges(graph.edges, related))
    }
  }, [selectedRepo, graph, setEdges])

  // ── Flow query ──────────────────────────────────────────────────────────
  const runFlow = async () => {
    if (!selectedRepo || !flowFrom.trim() || !flowTo.trim()) return
    setFlowLoading(true)
    setFlowResult(null)
    try {
      const res = await fetch(
        `/api/repositories/${selectedRepo}/flow?from=${encodeURIComponent(flowFrom)}&to=${encodeURIComponent(flowTo)}`
      )
      const data: FlowResult = await res.json()
      setFlowResult(data)
      if (data.pathFound && data.steps.length > 0) {
        const ids = new Set(data.steps.map(s => s.nodeId))
        setHighlighted(ids)
        if (graph) setEdges(buildEdges(graph.edges, ids))
      }
    } catch { setFlowResult({ pathFound: false, from: flowFrom, to: flowTo, message: 'Request failed', steps: [] }) }
    finally { setFlowLoading(false) }
  }

  const clearHighlight = () => {
    setHighlighted(new Set())
    setSelectedModule(null)
    setModuleDetail(null)
    if (graph) setEdges(buildEdges(graph.edges, new Set()))
    setSearchQuery('')
    setFlowResult(null)
  }

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div className="arch-page">
      {/* ── Header ── */}
      <header className="page-header">
        <div>
          <h1 className="page-title">Architecture</h1>
          <p className="page-subtitle">Module-level dependency graph derived from repository analysis</p>
        </div>
      </header>

      {/* ── Toolbar ── */}
      <div className="arch-toolbar">
        {/* Repo selector */}
        <div className="arch-toolbar-group">
          <label className="arch-toolbar-label">Repository</label>
          <select
            className="arch-select"
            value={selectedRepo ?? ''}
            onChange={e => setSelectedRepo(Number(e.target.value))}
          >
            {repositories.length === 0
              ? <option>No repositories analyzed yet</option>
              : repositories.map(r => <option key={r.id} value={r.id}>{r.name}</option>)
            }
          </select>
        </div>

        {/* Search */}
        <div className="arch-toolbar-group">
          <label className="arch-toolbar-label">Search</label>
          <input
            className="arch-search"
            placeholder="Module, layer, or path…"
            value={searchQuery}
            onChange={e => setSearchQuery(e.target.value)}
          />
        </div>

        {/* Flow */}
        <div className="arch-toolbar-group arch-toolbar-flow">
          <label className="arch-toolbar-label">Dependency Flow</label>
          <div className="arch-flow-row">
            <input className="arch-flow-input" placeholder="From (module/file)" value={flowFrom}
              onChange={e => setFlowFrom(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter') runFlow() }} />
            <span className="arch-flow-arrow">→</span>
            <input className="arch-flow-input" placeholder="To (module/file)" value={flowTo}
              onChange={e => setFlowTo(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter') runFlow() }} />
            <button className="arch-flow-btn" onClick={runFlow} disabled={flowLoading}>
              {flowLoading ? '…' : 'Trace'}
            </button>
          </div>
        </div>

        {highlighted.size > 0 && (
          <button className="arch-clear-btn" onClick={clearHighlight}>Clear highlight</button>
        )}
      </div>

      {/* ── Flow result banner ── */}
      {flowResult && (
        <div className={`arch-flow-result ${flowResult.pathFound ? 'arch-flow-result--found' : 'arch-flow-result--none'}`}>
          <strong>{flowResult.pathFound ? '✓ Path found' : '✗ No path'}</strong>
          {' — '}{flowResult.message}
          {flowResult.pathFound && flowResult.steps.length > 0 && (
            <span className="arch-flow-steps">
              {flowResult.steps.map((s, i) => (
                <span key={s.nodeId}>
                  <code>{s.name}</code>{i < flowResult.steps.length - 1 ? ' → ' : ''}
                </span>
              ))}
            </span>
          )}
        </div>
      )}

      {/* ── Main area ── */}
      <div className="arch-main">
        {/* Graph panel */}
        <div className="arch-graph-panel">
          {loading && <div className="arch-overlay">Loading architecture…</div>}
          {error && <div className="arch-overlay arch-overlay--error">{error}</div>}
          {!loading && !error && graph && graph.nodes.length === 0 && (
            <div className="arch-overlay">
              No modules detected for this repository.<br />
              <small>Analyze the repository first using the Analyze page.</small>
            </div>
          )}
          {!loading && !error && !graph && repositories.length === 0 && (
            <div className="arch-overlay">
              No repositories analyzed yet.<br />
              <small>Go to Analyze Repository to add one.</small>
            </div>
          )}

          <ReactFlow
            nodes={nodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onNodeClick={onNodeClick}
            fitView
            fitViewOptions={{ padding: 0.3 }}
            minZoom={0.2}
            maxZoom={2}
            proOptions={{ hideAttribution: true }}
          >
            <Background gap={20} color="#212836" />
            <Controls />
            <MiniMap
              nodeColor={n => {
                const archNode = (n.data as { archNode?: ArchNode }).archNode
                if (!archNode) return '#1b212d'
                const style = getLayerStyle(archNode.layer)
                return highlighted.size > 0
                  ? (highlighted.has(n.id) ? style.border : '#212836')
                  : style.border
              }}
              style={{ background: '#090d16', border: '1px solid #212836', borderRadius: 6 }}
            />
          </ReactFlow>

          {/* Interactive Legend */}
          {graph && graph.nodes.length > 0 && (
            <div className="arch-legend">
              {[...new Set(graph.nodes.map(n => n.layer))].sort().map(layer => {
                const s = getLayerStyle(layer)
                const count = graph.nodes.filter(n => n.layer === layer).length
                return (
                  <div
                    key={layer}
                    className="arch-legend-item"
                    onClick={() => {
                      const matches = new Set(graph.nodes.filter(n => n.layer === layer).map(n => n.id))
                      setHighlighted(matches)
                      setEdges(buildEdges(graph.edges, matches))
                    }}
                    style={{ cursor: 'pointer' }}
                    title={`Click to filter ${layer} modules`}
                  >
                    <span className="arch-legend-dot" style={{ background: s.border }} />
                    <span>{layer}</span>
                    <span style={{ fontSize: 10, opacity: 0.65 }}>({count})</span>
                  </div>
                )
              })}
            </div>
          )}
        </div>

        {/* Right panel: module list + detail */}
        <div className="arch-right-panel">
          {/* Module list */}
          {graph && graph.nodes.length > 0 && (
            <div className="arch-module-list">
              <div className="arch-panel-title">
                Modules
                <span className="arch-badge">{graph.nodes.length}</span>
              </div>
              <div className="arch-module-scroll">
                {graph.nodes
                  .filter(n => !searchQuery || n.name.toLowerCase().includes(searchQuery.toLowerCase())
                    || n.path.toLowerCase().includes(searchQuery.toLowerCase()))
                  .map(n => {
                    const style = getLayerStyle(n.layer)
                    return (
                      <div
                        key={n.id}
                        className={`arch-module-item ${selectedModule?.id === n.id ? 'arch-module-item--selected' : ''} ${highlighted.size > 0 && !highlighted.has(n.id) ? 'arch-module-item--dim' : ''}`}
                        onClick={() => {
                          setSelectedModule(n)
                          setDetailLoading(true)
                          setModuleDetail(null)
                          const moduleId = parseInt(n.id.replace('module-', ''))
                          fetch(`/api/repositories/${selectedRepo}/modules/${moduleId}`)
                            .then(r => r.ok ? r.json() : null)
                            .then(setModuleDetail)
                            .catch(() => setModuleDetail(null))
                            .finally(() => setDetailLoading(false))
                        }}
                      >
                        <span className="arch-module-dot" style={{ background: style.border }} />
                        <div className="arch-module-info">
                          <div className="arch-module-name">{n.name}</div>
                          <div className="arch-module-meta">{n.layer} · {n.fileCount} files</div>
                        </div>
                      </div>
                    )
                  })}
              </div>
            </div>
          )}

          {/* Module detail */}
          {selectedModule && (
            <div className="arch-detail">
              <div className="arch-panel-title">
                {selectedModule.name}
                <button className="arch-detail-close" onClick={() => { setSelectedModule(null); setModuleDetail(null) }}>×</button>
              </div>

              {detailLoading && <div className="arch-detail-loading">Loading…</div>}

              {moduleDetail && !detailLoading && (
                <>
                  <div className="arch-detail-grid">
                    <span className="arch-detail-key">Layer</span>
                    <span>{moduleDetail.layer}</span>
                    <span className="arch-detail-key">Type</span>
                    <span>{moduleDetail.moduleType}</span>
                    <span className="arch-detail-key">Path</span>
                    <code className="code-badge" style={{ fontSize: 11 }}>{moduleDetail.path}</code>
                    <span className="arch-detail-key">Files</span>
                    <span>{moduleDetail.fileCount}</span>
                    <span className="arch-detail-key">Outgoing deps</span>
                    <span>{moduleDetail.outgoingDependencies.length} modules</span>
                    <span className="arch-detail-key">Incoming deps</span>
                    <span>{moduleDetail.incomingDependencies.length} modules</span>
                  </div>

                  {moduleDetail.outgoingDependencies.length > 0 && (
                    <div className="arch-detail-section">
                      <div className="arch-detail-subtitle">Depends on</div>
                      {moduleDetail.outgoingDependencies.map(d => (
                        <div key={d.moduleId} className="arch-dep-item arch-dep-item--out">
                          <span>→ {d.moduleName}</span>
                          <span className="arch-dep-count">{d.dependencyCount}</span>
                        </div>
                      ))}
                    </div>
                  )}

                  {moduleDetail.incomingDependencies.length > 0 && (
                    <div className="arch-detail-section">
                      <div className="arch-detail-subtitle">Used by</div>
                      {moduleDetail.incomingDependencies.map(d => (
                        <div key={d.moduleId} className="arch-dep-item arch-dep-item--in">
                          <span>← {d.moduleName}</span>
                          <span className="arch-dep-count">{d.dependencyCount}</span>
                        </div>
                      ))}
                    </div>
                  )}

                  {moduleDetail.files.length > 0 && (
                    <div className="arch-detail-section">
                      <div className="arch-detail-subtitle">Files ({moduleDetail.files.length})</div>
                      <div className="arch-file-list">
                        {moduleDetail.files.slice(0, 30).map(f => (
                          <div key={f.id} className="arch-file-item">
                            <span className="arch-file-dot" style={{ background: langColour(f.language) }} />
                            <span className="arch-file-name" title={f.filePath}>{f.fileName}</span>
                            <span className="arch-file-size">{fmtBytes(f.fileSize)}</span>
                          </div>
                        ))}
                        {moduleDetail.files.length > 30 && (
                          <div className="arch-file-more">+{fmt(moduleDetail.files.length - 30)} more files</div>
                        )}
                      </div>
                    </div>
                  )}
                </>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
