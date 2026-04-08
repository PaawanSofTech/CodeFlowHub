import React, { useEffect, useState, useCallback } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { GitCommit, Filter, Search, RefreshCw } from 'lucide-react'
import { commitsApi, reposApi } from '../api/client'
import { Card, Badge, Spinner, HashChip, EmptyState, Toast } from '../components/Layout'
import { useRepoSocket } from '../hooks/useRepoSocket'
import { formatDistanceToNow } from 'date-fns'

export function CommitsPage() {
  const { owner, name } = useParams<{ owner: string; name: string }>()
  const [searchParams, setSearchParams] = useSearchParams()
  const [commits, setCommits] = useState<any[]>([])
  const [branches, setBranches] = useState<any[]>([])
  const [loading, setLoading] = useState(true)
  // UI input state (typing)
  const [authorInput, setAuthorInput] = useState(searchParams.get('author') || '')
  const [branchInput, setBranchInput] = useState(searchParams.get('branch') || '')

  // Applied filters (actual API state)
  const [author, setAuthor] = useState(authorInput)
  const [branch, setBranch] = useState(branchInput)
  const [viewMode, setViewMode] = useState<'list' | 'graph'>('list')
  const [toast, setToast] = useState<string | null>(null)

  const load = useCallback(async (silent = false) => {
    if (!owner || !name) return
    if (!silent) setLoading(true)
    try {
      const [c, b] = await Promise.all([
        commitsApi.list(owner, name, { branch: branch || undefined, author: author || undefined, limit: 100 }),
        reposApi.branches(owner, name),
      ])
      setCommits(c)
      setBranches(b)
    } finally {
      if (!silent) setLoading(false)
    }
  }, [owner, name, branch, author])

  useEffect(() => { load() }, [load])

  // Real-time: refresh when CLI pushes
  useRepoSocket({
    owner: owner || '',
    name: name || '',
    onEvent: (event) => {
      if (event.type === 'PushReceived') {
        setToast(`Push on "${event.branch}" — refreshing commits...`)
        load(true)
      }
    },
  })

  const applyFilters = () => {
    const p: Record<string, string> = {}

    if (authorInput) p.author = authorInput
    if (branchInput) p.branch = branchInput

    // commit to real state
    setAuthor(authorInput)
    setBranch(branchInput)

    // sync URL
    setSearchParams(p)
  }

  if (loading) return <Spinner />

  return (
    <div className="space-y-5">
      {toast && <Toast message={toast} type="success" onDismiss={() => setToast(null)} />}

      {/* Header */}
      <div className="flex items-center gap-3 flex-wrap">
        <Link to={`/${owner}/${name}`} className="text-gray-500 hover:text-gray-300 text-sm transition-colors">{owner}/{name}</Link>
        <span className="text-gray-700">/</span>
        <h1 className="text-white font-semibold">Commits</h1>
        <Badge color="gray">{commits.length}</Badge>
        <div className="flex-1" />
        <button onClick={() => load(true)} className="p-1.5 text-gray-500 hover:text-gray-300 rounded transition-colors">
          <RefreshCw size={14} />
        </button>
        <div className="flex items-center gap-1 bg-gray-800 border border-gray-700 rounded-lg p-1">
          {(['list', 'graph'] as const).map(m => (
            <button
              key={m}
              onClick={() => setViewMode(m)}
              className={`px-3 py-1 text-sm rounded-md transition-colors capitalize ${viewMode === m ? 'bg-gray-700 text-white' : 'text-gray-500 hover:text-gray-300'
                }`}
            >
              {m}
            </button>
          ))}
        </div>
      </div>

      {/* Filters */}
      <Card className="p-4">
        <div className="flex items-center gap-3 flex-wrap">
          <Filter size={14} className="text-gray-500" />
          <select
            value={branch}
            onChange={e => setBranch(e.target.value)}
            className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-sm text-white focus:outline-none focus:border-blue-500"
          >
            <option value="">All branches</option>
            {branches.map((b: any) => <option key={b.name} value={b.name}>{b.name}</option>)}
          </select>
          <input
            value={author}
            onChange={e => setAuthor(e.target.value)}
            placeholder="Filter by author..."
            className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-sm text-white focus:outline-none focus:border-blue-500 w-48"
          />
          <button
            onClick={applyFilters}
            className="bg-blue-600 hover:bg-blue-500 text-white px-3 py-1.5 text-sm rounded-lg flex items-center gap-1.5 transition-colors"
          >
            <Search size={13} /> Apply
          </button>
          {(author || branch) && (
            <button
              onClick={() => {
                setAuthor('')
                setBranch('')
                setAuthorInput('')
                setBranchInput('')
                setSearchParams({})
              }}
              className="text-sm text-gray-500 hover:text-gray-300 transition-colors"
            >
              Clear
            </button>
          )}
        </div>
      </Card>

      {/* Content */}
      {commits.length === 0 ? (
        <EmptyState icon={<GitCommit size={40} />} title="No commits" description="No commits match your filters." />
      ) : viewMode === 'list' ? (
        <CommitList commits={commits} owner={owner!} name={name!} />
      ) : (
        <CommitGraph commits={commits} owner={owner!} name={name!} />
      )}
    </div>
  )
}

function CommitList({ commits, owner, name }: { commits: any[]; owner: string; name: string }) {
  const grouped: Record<string, any[]> = {}
  commits.forEach(c => {
    const day = new Date(c.timestamp).toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' })
    if (!grouped[day]) grouped[day] = []
    grouped[day].push(c)
  })

  return (
    <div className="space-y-6">
      {Object.entries(grouped).map(([day, dayCommits]) => (
        <div key={day}>
          <div className="text-sm text-gray-500 font-medium mb-3 flex items-center gap-2">
            <div className="h-px flex-1 bg-gray-800" />
            <span className="px-3">{day} — {dayCommits.length} commit{dayCommits.length !== 1 ? 's' : ''}</span>
            <div className="h-px flex-1 bg-gray-800" />
          </div>
          <Card>
            <div className="divide-y divide-gray-800">
              {dayCommits.map((c: any) => (
                <div key={c.hash} className="flex items-start gap-4 p-4 hover:bg-gray-800/40 transition-colors group">
                  <div className="flex flex-col items-center pt-1 flex-shrink-0">
                    <div className={`w-3 h-3 rounded-full border-2 ${c.isMerge ? 'border-purple-400 bg-purple-900' : 'border-blue-400 bg-blue-900'}`} />
                  </div>

                  <div className="flex-1 min-w-0">
                    <Link
                      to={`/${owner}/${name}/commits/${c.hash}`}
                      className="text-white hover:text-blue-400 font-medium text-sm group-hover:text-blue-400 transition-colors"
                    >
                      {c.message}
                    </Link>
                    <div className="flex items-center gap-2 mt-1 text-xs text-gray-500 flex-wrap">
                      <span className="text-gray-400">{c.author}</span>
                      <span>·</span>
                      <span>{formatDistanceToNow(new Date(c.timestamp), { addSuffix: true })}</span>
                      {c.isMerge && <Badge color="purple">merge</Badge>}
                      {c.branch && <Badge color="gray">{c.branch}</Badge>}
                    </div>
                    {c.changes?.length > 0 && (
                      <div className="flex gap-1 mt-1.5 flex-wrap">
                        {c.changes.slice(0, 5).map((f: string) => (
                          <span key={f} className="text-xs bg-gray-800 text-gray-400 px-1.5 py-0.5 rounded font-mono">{f}</span>
                        ))}
                        {c.changes.length > 5 && (
                          <span className="text-xs text-gray-600">+{c.changes.length - 5} more</span>
                        )}
                      </div>
                    )}
                  </div>

                  <div className="flex items-center gap-2 flex-shrink-0">
                    <HashChip hash={c.hash} />
                    <Link
                      to={`/${owner}/${name}/commits/${c.hash}`}
                      className="opacity-0 group-hover:opacity-100 text-xs text-blue-400 hover:text-blue-300 transition-opacity"
                    >
                      View →
                    </Link>
                  </div>
                </div>
              ))}
            </div>
          </Card>
        </div>
      ))}
    </div>
  )
}
function CommitGraph({ commits, owner, name }: { commits: any[]; owner: string; name: string }) {
  const COLORS = ['#3b82f6', '#a855f7', '#22c55e', '#f59e0b', '#ef4444', '#06b6d4', '#ec4899']

  const branchColors: Record<string, string> = {}
  let colorIdx = 0

  commits.forEach(c => {
    if (c.branch && !branchColors[c.branch]) {
      branchColors[c.branch] = COLORS[colorIdx++ % COLORS.length]
    }
  })

  // 🔥 DAG-based lane system
  const lanes: string[] = []
  const laneMap: Record<string, number> = {}

  commits.forEach(c => {
    if (!(c.hash in laneMap)) {
      laneMap[c.hash] = lanes.length
      lanes.push(c.hash)
    }

    const currentLane = laneMap[c.hash]

    // assign parents to same lane (or new lane)
    c.parents?.forEach((p: string, idx: number) => {
      if (!(p in laneMap)) {
        if (idx === 0) {
          laneMap[p] = currentLane // main parent continues
        } else {
          laneMap[p] = lanes.length // side lane
          lanes.push(p)
        }
      }
    })
  })

  return (
    <Card className="overflow-x-auto">
      <div className="p-4 space-y-1 min-w-[700px]">
        {commits.map((c: any, idx) => {
          const laneIndex = laneMap[c.hash] ?? 0
          const color = branchColors[c.branch] || '#3b82f6'

          return (
            <div key={c.hash} className="flex items-center gap-3 py-2 hover:bg-gray-800/40 rounded-lg px-2">
              
              {/* 🔥 GRAPH */}
              <div className="flex gap-2 min-w-[100px] relative">
                {lanes.map((_, i) => (
                  <div key={i} className="relative w-4 flex flex-col items-center">
                    
                    {/* vertical line */}
                    {idx < commits.length - 1 && (
                      <div className="absolute top-3 w-0.5 h-full bg-gray-700" />
                    )}

                    {/* node */}
                    {i === laneIndex ? (
                      <div
                        className="w-3 h-3 rounded-full border-2 z-10"
                        style={{
                          borderColor: color,
                          backgroundColor: color + '30'
                        }}
                      />
                    ) : (
                      <div className="w-2 h-2 rounded-full bg-gray-700 mt-[2px]" />
                    )}
                  </div>
                ))}

                {/* 🔥 REAL MERGE LINES */}
                {c.parents?.length > 1 &&
                  c.parents.slice(1).map((p: string, i: number) => {
                    const parentLane = laneMap[p]
                    if (parentLane === undefined) return null

                    const left = Math.min(laneIndex, parentLane) * 16
                    const width = Math.abs(laneIndex - parentLane) * 16

                    return (
                      <div
                        key={i}
                        className="absolute h-0.5 bg-purple-500 top-2"
                        style={{ left, width }}
                      />
                    )
                  })}
              </div>

              {/* TEXT */}
              <div className="flex-1 min-w-0 flex items-center gap-3">
                <Link
                  to={`/${owner}/${name}/commits/${c.hash}`}
                  className="text-sm text-gray-300 hover:text-blue-400 truncate"
                >
                  {c.message}
                </Link>
                {c.isMerge && <Badge color="purple">merge</Badge>}
              </div>

              {/* META */}
              <div className="flex items-center gap-2 text-xs text-gray-600">
                <span className="hidden sm:block">{c.author}</span>
                <HashChip hash={c.hash} />
                {c.branch && (
                  <span style={{ color }} className="font-medium">
                    {c.branch}
                  </span>
                )}
              </div>
            </div>
          )
        })}
      </div>
    </Card>
  )
}