import React, { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { GitCommit, GitMerge, Filter, Search } from 'lucide-react'
import { commitsApi, reposApi } from '../api/client'
import { Card, Badge, Spinner, HashChip, EmptyState } from '../components/Layout'
import { formatDistanceToNow } from 'date-fns'

export function CommitsPage() {
  const { owner, name } = useParams<{ owner: string; name: string }>()
  const [searchParams, setSearchParams] = useSearchParams()
  const [commits, setCommits] = useState<any[]>([])
  const [branches, setBranches] = useState<any[]>([])
  const [loading, setLoading] = useState(true)
  const [author, setAuthor] = useState(searchParams.get('author') || '')
  const [branch, setBranch] = useState(searchParams.get('branch') || '')
  const [viewMode, setViewMode] = useState<'list' | 'graph'>('list')

  useEffect(() => {
    if (!owner || !name) return
    Promise.all([
      commitsApi.list(owner, name, { branch: branch || undefined, author: author || undefined, limit: 50 }),
      reposApi.branches(owner, name),
    ]).then(([c, b]) => { setCommits(c); setBranches(b) })
    .finally(() => setLoading(false))
  }, [owner, name, branch, author])

  const applyFilters = () => {
    const p: Record<string, string> = {}
    if (author) p.author = author
    if (branch) p.branch = branch
    setSearchParams(p)
  }

  if (loading) return <Spinner />

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex items-center gap-3">
        <Link to={`/${owner}/${name}`} className="text-gray-500 hover:text-gray-300 text-sm">{owner}/{name}</Link>
        <span className="text-gray-700">/</span>
        <h1 className="text-white font-semibold">Commits</h1>
        <Badge color="gray">{commits.length}</Badge>
        <div className="flex-1" />
        <div className="flex items-center gap-2">
          <button
            onClick={() => setViewMode('list')}
            className={`px-3 py-1.5 text-sm rounded-lg border transition-colors ${viewMode === 'list' ? 'border-blue-500 text-blue-400' : 'border-gray-700 text-gray-500 hover:text-gray-300'}`}
          >
            List
          </button>
          <button
            onClick={() => setViewMode('graph')}
            className={`px-3 py-1.5 text-sm rounded-lg border transition-colors ${viewMode === 'graph' ? 'border-blue-500 text-blue-400' : 'border-gray-700 text-gray-500 hover:text-gray-300'}`}
          >
            Graph
          </button>
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
        </div>
      </Card>

      {/* Commits */}
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
  // Group by date
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
            <span className="px-3">{day}</span>
            <div className="h-px flex-1 bg-gray-800" />
          </div>
          <Card>
            <div className="divide-y divide-gray-800">
              {dayCommits.map((c: any) => (
                <div key={c.hash} className="flex items-start gap-4 p-4 hover:bg-gray-800/40 transition-colors group">
                  {/* Graph line */}
                  <div className="flex flex-col items-center pt-1">
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
                      <span>committed</span>
                      <span>{formatDistanceToNow(new Date(c.timestamp), { addSuffix: true })}</span>
                      {c.isMerge && <Badge color="purple">merge</Badge>}
                      <Badge color="gray">{c.branch}</Badge>
                    </div>
                    {c.changes?.length > 0 && (
                      <div className="flex gap-1 mt-1.5 flex-wrap">
                        {c.changes.slice(0, 4).map((f: string) => (
                          <span key={f} className="text-xs bg-gray-800 text-gray-400 px-1.5 py-0.5 rounded font-mono">{f}</span>
                        ))}
                        {c.changes.length > 4 && (
                          <span className="text-xs text-gray-600">+{c.changes.length - 4} more</span>
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
  // Assign branch lanes by tracking which commits belong to which branch
  const COLORS = ['#3b82f6', '#a855f7', '#22c55e', '#f59e0b', '#ef4444', '#06b6d4']
  const branchColors: Record<string, string> = {}
  let colorIdx = 0

  commits.forEach(c => {
    if (c.branch && !branchColors[c.branch]) {
      branchColors[c.branch] = COLORS[colorIdx++ % COLORS.length]
    }
  })

  return (
    <Card className="overflow-x-auto">
      <div className="p-4 space-y-1 min-w-[600px]">
        {commits.map((c: any, idx) => {
          const color = branchColors[c.branch] || '#3b82f6'
          return (
            <div key={c.hash} className="flex items-center gap-3 group py-1 hover:bg-gray-800/40 rounded-lg px-2 transition-colors">
              {/* Graph dot */}
              <div className="relative flex-shrink-0 w-4 flex items-center justify-center">
                {idx < commits.length - 1 && (
                  <div className="absolute top-3 bottom-0 left-1/2 w-0.5" style={{ backgroundColor: color + '60' }} />
                )}
                <div
                  className="w-3 h-3 rounded-full border-2 z-10 relative"
                  style={{ borderColor: color, backgroundColor: color + '30' }}
                />
              </div>

              {/* Content */}
              <div className="flex-1 min-w-0 flex items-center gap-3">
                <Link
                  to={`/${owner}/${name}/commits/${c.hash}`}
                  className="text-sm text-gray-300 hover:text-blue-400 transition-colors truncate"
                >
                  {c.message}
                </Link>
                {c.isMerge && <Badge color="purple">merge</Badge>}
              </div>

              {/* Meta */}
              <div className="flex items-center gap-2 flex-shrink-0 text-xs text-gray-600">
                <span>{c.author}</span>
                <HashChip hash={c.hash} />
                <span style={{ color }}>{c.branch}</span>
              </div>
            </div>
          )
        })}
      </div>
    </Card>
  )
}
