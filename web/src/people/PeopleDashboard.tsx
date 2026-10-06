import ArrowForwardIcon from '@mui/icons-material/ArrowForward'
import { Button } from '@mui/material'
import type { ReactNode } from 'react'
import { Link as RouterLink } from 'react-router'
import { unknownLabels, usePeople } from '../api/people'
import { EmptyMessage } from '../shared/EmptyMessage'
import { PersonAvatar } from './PersonAvatar'

const NEXT_UP = 5

function StatCard({ label, value, accent = false }: { label: string; value: number; accent?: boolean }) {
  return (
    <div
      className={`flex flex-col gap-1 rounded-2xl border border-solid p-4 shadow-sm ${
        accent
          ? 'border-[#d9d7f7] bg-[#ecebfb] dark:border-indigo-500/30 dark:bg-indigo-500/10'
          : 'border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900'
      }`}
    >
      <span
        className={`text-2xl font-semibold tabular-nums tracking-tight ${
          accent ? 'text-[#4b46c4] dark:text-indigo-300' : 'text-zinc-900 dark:text-zinc-100'
        }`}
      >
        {value}
      </span>
      <span className="text-sm text-zinc-500">{label}</span>
    </div>
  )
}

function Panel({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="rounded-2xl border border-solid border-zinc-200 bg-white p-4 shadow-sm dark:border-zinc-800 dark:bg-zinc-900">
      <h2 className="m-0 mb-3 text-sm font-semibold tracking-tight text-zinc-900 dark:text-zinc-100">{title}</h2>
      {children}
    </section>
  )
}

/** Shown when no person is selected: what is waiting for review, and where to go next. */
export function PeopleDashboard() {
  const people = usePeople()
  const all = people.data ?? []

  if (all.length === 0) return <EmptyMessage>Select a person to see their photos.</EmptyMessage>

  const labels = unknownLabels(all)
  const named = all.filter((person) => person.name !== null)
  const unknown = all.filter((person) => person.name === null)
  const suggested = all.reduce((sum, person) => sum + person.suggestedImageCount, 0)
  const next = all
    .filter((person) => person.suggestedImageCount > 0)
    .sort((a, b) => b.suggestedImageCount - a.suggestedImageCount || a.id - b.id)[0]
  const nextLabel = next ? (next.name ?? labels.get(next.id) ?? 'Unknown') : null
  const biggestUnknown = [...unknown]
    .sort((a, b) => b.suggestedImageCount - a.suggestedImageCount || a.id - b.id)
    .slice(0, NEXT_UP)

  return (
    <div className="flex flex-col gap-6 overflow-y-auto p-8">
      <div>
        <h1 className="m-0 text-xl font-semibold tracking-tight text-zinc-900 dark:text-zinc-100">
          Review people
        </h1>
        <p className="m-0 mt-1 text-sm text-zinc-500">Select a person to see their photos.</p>
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatCard label="Named people" value={named.length} />
        <StatCard label="Unknown groups" value={unknown.length} />
        <StatCard label="Suggestions waiting" value={suggested} accent={suggested > 0} />
      </div>
      {next && nextLabel && (
        <div>
          <Button
            component={RouterLink}
            to={`/people/${next.id}`}
            variant="contained"
            disableElevation
            endIcon={<ArrowForwardIcon fontSize="small" />}
            sx={{ borderRadius: '12px', textTransform: 'none', fontWeight: 500 }}
          >
            Review next: {nextLabel} ({next.suggestedImageCount})
          </Button>
        </div>
      )}
      {biggestUnknown.length > 0 && (
        <Panel title="Biggest unknown groups">
          <ul className="m-0 grid list-none grid-cols-2 gap-2 p-0 sm:grid-cols-3 lg:grid-cols-5">
            {biggestUnknown.map((person) => (
              <li key={person.id}>
                <RouterLink
                  to={`/people/${person.id}`}
                  style={{ textDecoration: 'none', color: 'inherit' }}
                  className="flex flex-col items-center gap-2 rounded-xl p-3 transition-all duration-200 ease-in-out hover:scale-[1.02] hover:bg-zinc-100 dark:hover:bg-zinc-800"
                >
                  <PersonAvatar faceId={person.coverFaceId} unknown className="h-16 w-16" />
                  <span className="text-sm font-medium text-zinc-900 dark:text-zinc-100">
                    {labels.get(person.id) ?? 'Unknown'}
                  </span>
                  <span className="text-xs text-zinc-500">{person.suggestedImageCount} suggested</span>
                </RouterLink>
              </li>
            ))}
          </ul>
        </Panel>
      )}
    </div>
  )
}
