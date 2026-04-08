import React, { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ShieldCheck, ShieldX, FileText, Plus, Minus, ChevronDown, ChevronRight } from 'lucide-react'
import { commitsApi } from '../api/client'
import { Card, Badge, Spinner, HashChip } from '../components/Layout'
import { format } from 'date-fns'

export function CommitPage() {
  const { owner, name, hash } = useParams<{ owner: string; name: string; hash: string }>()
  const [commit, setCommit] = useState<any>(null)
  const [diffs, setDiffs] = useState<any[]>([])
  const [loading, setLoading] = useState(true)
  const [expandedFiles, setExpandedFiles] = useState<Set<string>>(new Set())

  useEffect(() => {
    if (!owner || !name || !hash) return
    Promise.all([
      commitsApi.get(owner, name, hash),
      commitsApi.diff(owner, name, hash),
    ]).then(([c, d]) => {
      setCommit(c)
      setDiffs(d)
      // Expand first 5 files by default
      setExpandedFiles(new Set(d.slice(0, 5).map((f: any) => f.filePath)))
    }).finally(() => setLoading(false))
  }, [owner, name, hash])

  const toggleFile = (path: string) => {
    setExpandedFiles(prev => {
      const next = new Set(prev)
      if (next.has(path)) next.delete(path)
      else next.add(path)
      return next
    })
  }

  const expandAll = () => setExpandedFiles(new Set(diffs.map(d => d.filePath)))
  const collapseAll = () => setExpandedFiles(new Set())

  if (loading) return <Spinner />
  if (!commit) return null

  const totalAdds = diffs.reduce((s: number, d: any) => s + (d.additions || 0), 0)
  const totalDels = diffs.reduce((s: number, d: any) => s + (d.deletions || 0), 0)

  return (
    <div className="space-y-5">
      {/* Breadcrumb */}
      <div className="text-sm text-gray-500 flex items-center gap-1 flex-wrap">
        <Link to="/" className="hover:text-blue-400 transition-colors">home</Link>
        <span>/</span>
        <Link to={`/${owner}/${name}`} className="hover:text-blue-400 transition-colors">{owner}/{name}</Link>
        <span>/</span>
        <Link to={`/${owner}/${name}/commits`} className="hover:text-blue-400 transition-colors">commits</Link>
        <span>/</span>
        <span className="text-gray-300 font-mono">{hash?.substring(0, 8)}</span>
      </div>

      {/* Commit info */}
      <Card className="p-6">
        <div className="flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <h1 className="text-xl font-semibold text-white mb-3 leading-snug">{commit.message}</h1>
            <div className="flex items-center gap-3 text-sm text-gray-400 flex-wrap">
              <div className="flex items-center gap-2">
                <div className="w-7 h-7 bg-blue-600 rounded-full flex items-center justify-center text-xs font-bold text-white flex-shrink-0">
                  {commit.author?.[0]?.toUpperCase() || '?'}
                </div>
                <span className="text-white font-medium">{commit.author}</span>
                {commit.email && <span className="text-gray-600 text-xs">&lt;{commit.email}&gt;</span>}
              </div>
              <span>committed</span>
              <span className="text-gray-300">{format(new Date(commit.timestamp), 'MMM d, yyyy HH:mm')}</span>
              {commit.branch && <Badge color="gray">{commit.branch}</Badge>}
            </div>

            {commit.parents?.length > 0 && (
              <div className="mt-3 text-xs text-gray-500 flex items-center gap-2 flex-wrap">
                <span>Parent{commit.parents.length > 1 ? 's' : ''}:</span>
                {commit.parents.map((p: string) => (
                  <Link key={p} to={`/${owner}/${name}/commits/${p}`} className="font-mono text-blue-400 hover:text-blue-300">
                    {p.substring(0, 8)}
                  </Link>
                ))}
                {commit.parents.length > 1 && <Badge color="purple">merge commit</Badge>}
              </div>
            )}
          </div>

          <div className="flex flex-col items-end gap-2 flex-shrink-0">
            <HashChip hash={commit.hash} />
            {commit.signatureValid !== undefined && (
              <div className={`flex items-center gap-1.5 text-xs px-2 py-1 rounded-full border ${
                commit.signatureValid
                  ? 'text-green-400 border-green-800 bg-green-950'
                  : 'text-red-400 border-red-800 bg-red-950'
              }`}>
                {commit.signatureValid ? <ShieldCheck size={12} /> : <ShieldX size={12} />}
                {commit.signatureValid ? 'Verified' : 'Unverified'}
              </div>
            )}
          </div>
        </div>
      </Card>

      {/* Diff summary bar */}
      {diffs.length > 0 && (
        <div className="flex items-center gap-4 text-sm">
          <span className="text-gray-400">
            {diffs.length} file{diffs.length !== 1 ? 's' : ''} changed
          </span>
          <span className="text-green-400 font-medium flex items-center gap-0.5">
            <Plus size={13} />{totalAdds}
          </span>
          <span className="text-red-400 font-medium flex items-center gap-0.5">
            <Minus size={13} />{totalDels}
          </span>
          <div className="flex gap-0.5 h-3 items-center">
            {Array.from({ length: Math.min(10, totalAdds + totalDels) }).map((_, i) => (
              <div
                key={i}
                className={`w-2.5 h-full rounded-sm ${i < Math.round(10 * totalAdds / Math.max(totalAdds + totalDels, 1)) ? 'bg-green-500' : 'bg-red-500'}`}
              />
            ))}
          </div>
          <div className="flex-1" />
          <button onClick={expandAll} className="text-xs text-gray-500 hover:text-gray-300 transition-colors">Expand all</button>
          <span className="text-gray-700">·</span>
          <button onClick={collapseAll} className="text-xs text-gray-500 hover:text-gray-300 transition-colors">Collapse all</button>
        </div>
      )}

      {/* File diffs */}
      <div className="space-y-3">
        {diffs.map((diff: any) => (
          <DiffFile
            key={diff.filePath}
            diff={diff}
            expanded={expandedFiles.has(diff.filePath)}
            onToggle={() => toggleFile(diff.filePath)}
            owner={owner!}
            name={name!}
            hash={hash!}
          />
        ))}
      </div>
    </div>
  )
}

function DiffFile({
  diff, expanded, onToggle, owner, name, hash,
}: {
  diff: any; expanded: boolean; onToggle: () => void
  owner: string; name: string; hash: string
}) {
  const statusColors: Record<string, string> = {
    Added: 'text-green-400 bg-green-900/30',
    Modified: 'text-yellow-400 bg-yellow-900/30',
    Deleted: 'text-red-400 bg-red-900/30',
  }

  return (
    <div className="border border-gray-800 rounded-lg overflow-hidden">
      <button
        onClick={onToggle}
        className="w-full flex items-center gap-3 p-3 bg-gray-900/80 hover:bg-gray-800/80 transition-colors text-left"
      >
        {expanded
          ? <ChevronDown size={14} className="text-gray-500 flex-shrink-0" />
          : <ChevronRight size={14} className="text-gray-500 flex-shrink-0" />}
        <FileText size={14} className="text-gray-500 flex-shrink-0" />
        <Link
          to={`/${owner}/${name}/tree/${hash}/${diff.filePath}`}
          onClick={e => e.stopPropagation()}
          className="font-mono text-sm text-gray-200 flex-1 min-w-0 truncate hover:text-blue-400 transition-colors"
        >
          {diff.filePath}
        </Link>
        <span className={`text-xs font-medium px-2 py-0.5 rounded flex-shrink-0 ${statusColors[diff.status] || 'text-gray-400 bg-gray-800'}`}>
          {diff.status}
        </span>
        {!diff.isBinary && (
          <span className="text-xs text-gray-500 flex-shrink-0 font-mono hidden sm:block">
            <span className="text-green-400">+{diff.additions}</span>
            {' '}
            <span className="text-red-400">-{diff.deletions}</span>
          </span>
        )}
      </button>

      {expanded && (
        <div className="overflow-x-auto bg-gray-950">
          {diff.isBinary ? (
            <div className="p-6 text-center text-gray-500 text-sm">Binary file — no diff available</div>
          ) : !diff.hunks?.length ? (
            <div className="p-6 text-center text-gray-500 text-sm">No textual changes</div>
          ) : (
            <table className="w-full text-xs font-mono border-collapse">
              <tbody>
                {diff.hunks?.map((hunk: any, hi: number) => (
                  <React.Fragment key={hi}>
                    <tr>
                      <td colSpan={3} className="bg-blue-950/30 text-blue-300/70 px-4 py-1 border-y border-gray-800/50 text-xs select-none">
                        @@ -{hunk.oldStart},{hunk.oldCount} +{hunk.newStart},{hunk.newCount} @@
                      </td>
                    </tr>
                    {hunk.lines?.map((line: any, li: number) => {
                      const isAdd = line.type === 'Addition'
                      const isDel = line.type === 'Deletion'
                      return (
                        <tr
                          key={li}
                          className={`${isAdd ? 'bg-green-950/30' : isDel ? 'bg-red-950/30' : ''}`}
                        >
                          <td className="text-gray-600 text-right pr-2 pl-3 py-0.5 select-none w-10 border-r border-gray-800/50 min-w-[2.5rem]">
                            {isDel ? line.old : ''}
                          </td>
                          <td className="text-gray-600 text-right pr-2 py-0.5 select-none w-10 border-r border-gray-800/50 min-w-[2.5rem]">
                            {isAdd ? line.new : ''}
                          </td>
                          <td className={`px-4 py-0.5 whitespace-pre ${isAdd ? 'text-green-300' : isDel ? 'text-red-300' : 'text-gray-400'}`}>
                            <span className={`mr-2 select-none ${isAdd ? 'text-green-500' : isDel ? 'text-red-500' : 'text-gray-700'}`}>
                              {isAdd ? '+' : isDel ? '-' : ' '}
                            </span>
                            {line.content}
                          </td>
                        </tr>
                      )
                    })}
                  </React.Fragment>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
    </div>
  )
}
