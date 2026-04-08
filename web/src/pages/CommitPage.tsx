import React, { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Shield, ShieldCheck, ShieldX, FileText, Plus, Minus, ChevronDown, ChevronRight, GitCommit } from 'lucide-react'
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

  if (loading) return <Spinner />
  if (!commit) return null

  const totalAdds = diffs.reduce((s: number, d: any) => s + d.additions, 0)
  const totalDels = diffs.reduce((s: number, d: any) => s + d.deletions, 0)

  return (
    <div className="space-y-5">
      {/* Breadcrumb */}
      <div className="text-sm text-gray-500">
        <Link to="/" className="hover:text-blue-400">home</Link>
        {' / '}
        <Link to={`/${owner}/${name}`} className="hover:text-blue-400">{owner}/{name}</Link>
        {' / '}
        <Link to={`/${owner}/${name}/commits`} className="hover:text-blue-400">commits</Link>
        {' / '}
        <span className="text-gray-300 font-mono">{hash?.substring(0, 8)}</span>
      </div>

      {/* Commit info */}
      <Card className="p-6">
        <div className="flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <h1 className="text-xl font-semibold text-white mb-3">{commit.message}</h1>
            <div className="flex items-center gap-3 text-sm text-gray-400 flex-wrap">
              <div className="flex items-center gap-2">
                <div className="w-6 h-6 bg-blue-600 rounded-full flex items-center justify-center text-xs font-bold text-white">
                  {commit.author?.[0]?.toUpperCase() || '?'}
                </div>
                <span className="text-white font-medium">{commit.author}</span>
              </div>
              <span>committed on</span>
              <span className="text-gray-300">{format(new Date(commit.timestamp), 'MMM d, yyyy HH:mm')}</span>
              <Badge color="gray">{commit.branch}</Badge>
            </div>

            {commit.parents?.length > 0 && (
              <div className="mt-3 text-xs text-gray-500">
                Parent{commit.parents.length > 1 ? 's' : ''}:{' '}
                {commit.parents.map((p: string) => (
                  <Link key={p} to={`/${owner}/${name}/commits/${p}`} className="font-mono text-blue-400 hover:text-blue-300 mr-2">
                    {p.substring(0, 8)}
                  </Link>
                ))}
                {commit.parents.length > 1 && <Badge color="purple">merge commit</Badge>}
              </div>
            )}
          </div>

          <div className="flex flex-col items-end gap-2">
            <HashChip hash={commit.hash} />
            {commit.signatureValid !== undefined && (
              <div className={`flex items-center gap-1.5 text-xs px-2 py-1 rounded-full border ${
                commit.signatureValid
                  ? 'text-green-400 border-green-800 bg-green-950'
                  : 'text-red-400 border-red-800 bg-red-950'
              }`}>
                {commit.signatureValid ? <ShieldCheck size={12} /> : <ShieldX size={12} />}
                {commit.signatureValid ? 'Signature verified' : 'Invalid signature'}
              </div>
            )}
          </div>
        </div>
      </Card>

      {/* Diff stats */}
      {diffs.length > 0 && (
        <div className="flex items-center gap-4 text-sm">
          <span className="text-gray-400">{diffs.length} files changed</span>
          <span className="text-green-400 font-medium">+{totalAdds}</span>
          <span className="text-red-400 font-medium">-{totalDels}</span>
          <div className="flex gap-0.5 h-3">
            {Array.from({ length: Math.min(10, totalAdds + totalDels) }).map((_, i) => (
              <div
                key={i}
                className={`w-3 rounded-sm ${i < Math.round(10 * totalAdds / Math.max(totalAdds + totalDels, 1)) ? 'bg-green-500' : 'bg-red-500'}`}
              />
            ))}
          </div>
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
          />
        ))}
      </div>
    </div>
  )
}

function DiffFile({ diff, expanded, onToggle }: { diff: any; expanded: boolean; onToggle: () => void }) {
  const statusColors: Record<string, string> = {
    Added: 'text-green-400', Modified: 'text-yellow-400', Deleted: 'text-red-400'
  }
  const statusBg: Record<string, string> = {
    Added: 'bg-green-900/30', Modified: 'bg-yellow-900/30', Deleted: 'bg-red-900/30'
  }

  return (
    <div className="border border-gray-800 rounded-lg overflow-hidden">
      {/* File header */}
      <button
        onClick={onToggle}
        className="w-full flex items-center gap-3 p-3 bg-gray-900 hover:bg-gray-800/80 transition-colors text-left"
      >
        {expanded ? <ChevronDown size={14} className="text-gray-500 flex-shrink-0" /> : <ChevronRight size={14} className="text-gray-500 flex-shrink-0" />}
        <FileText size={14} className="text-gray-500 flex-shrink-0" />
        <span className="font-mono text-sm text-gray-200 flex-1 min-w-0 truncate">{diff.filePath}</span>
        <span className={`text-xs font-medium px-2 py-0.5 rounded ${statusColors[diff.status]} ${statusBg[diff.status]}`}>
          {diff.status}
        </span>
        {!diff.isBinary && (
          <span className="text-xs text-gray-500 flex-shrink-0 font-mono">
            <span className="text-green-400">+{diff.additions}</span>
            {' '}
            <span className="text-red-400">-{diff.deletions}</span>
          </span>
        )}
      </button>

      {/* Diff content */}
      {expanded && (
        <div className="overflow-x-auto">
          {diff.isBinary ? (
            <div className="p-4 text-center text-gray-500 text-sm">Binary file changed</div>
          ) : diff.hunks?.length === 0 ? (
            <div className="p-4 text-center text-gray-500 text-sm">No changes</div>
          ) : (
            <table className="w-full text-xs font-mono border-collapse">
              <tbody>
                {diff.hunks?.map((hunk: any, hi: number) => (
                  <React.Fragment key={hi}>
                    <tr>
                      <td colSpan={3} className="bg-blue-950/40 text-blue-300 px-4 py-1 border-y border-gray-800">
                        @@ -{hunk.oldStart},{hunk.oldCount} +{hunk.newStart},{hunk.newCount} @@
                      </td>
                    </tr>
                    {hunk.lines?.map((line: any, li: number) => {
                      const isAdd = line.type === 'Addition'
                      const isDel = line.type === 'Deletion'
                      return (
                        <tr
                          key={li}
                          className={`${isAdd ? 'bg-green-950/40' : isDel ? 'bg-red-950/40' : ''} hover:brightness-110`}
                        >
                          <td className="text-gray-600 text-right pr-3 pl-4 py-0.5 select-none w-12 border-r border-gray-800 min-w-[3rem]">
                            {isDel ? line.old : ''}
                          </td>
                          <td className="text-gray-600 text-right pr-3 py-0.5 select-none w-12 border-r border-gray-800 min-w-[3rem]">
                            {isAdd ? line.new : ''}
                          </td>
                          <td className={`px-4 py-0.5 whitespace-pre ${isAdd ? 'text-green-300' : isDel ? 'text-red-300' : 'text-gray-300'}`}>
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
