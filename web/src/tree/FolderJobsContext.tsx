import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'
import { startDiscovery, startScan, useJobEvents, type JobKind } from '../api/jobs'
import { ApiError } from '../api/client'
import type { DiscoveryProgress, ScanProgress } from '../api/types'
import { useNotify } from '../app/notify'

export type ActiveJob =
  | { kind: 'discoveries'; folderId: number; progress: DiscoveryProgress | null }
  | { kind: 'scans'; folderId: number; progress: ScanProgress | null }

type FolderJobs = {
  activeJob: ActiveJob | null
  refreshFolder: (folderId: number) => void
  scanFolder: (folderId: number, isRecursive: boolean) => void
}

const FolderJobsContext = createContext<FolderJobs | null>(null)

function startErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) return 'A discovery or scan is already in progress.'
    const fieldError = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (fieldError) return fieldError
  }
  return "Couldn't start the job."
}

/** Tracks the single discovery/scan job the backend allows at a time, shared by the whole tree. */
export function FolderJobsProvider({ children }: { children: ReactNode }) {
  const [job, setJob] = useState<{ kind: JobKind; folderId: number; jobId: number } | null>(null)
  const queryClient = useQueryClient()
  const notify = useNotify()
  const progress = useJobEvents(job?.kind ?? 'discoveries', job?.jobId ?? null, (event) => {
    if (event.status === 'Failed') {
      notify(event.errorMessage ?? 'The job failed.')
      setJob(null)
    } else if (event.status === 'Completed' || event.status === 'Cancelled') {
      void queryClient.invalidateQueries({ queryKey: ['folders'] })
      setJob(null)
    }
  })

  const run = useCallback(
    (kind: JobKind, folderId: number, action: () => Promise<number>) => {
      action()
        .then((jobId) => setJob({ kind, folderId, jobId }))
        .catch((error: unknown) => notify(startErrorMessage(error)))
    },
    [notify],
  )

  const refreshFolder = useCallback(
    (folderId: number) => run('discoveries', folderId, () => startDiscovery(folderId)),
    [run],
  )
  const scanFolder = useCallback(
    (folderId: number, isRecursive: boolean) =>
      run('scans', folderId, () => startScan(folderId, isRecursive)),
    [run],
  )

  const activeJob: ActiveJob | null =
    job === null
      ? null
      : job.kind === 'discoveries'
        ? {
            kind: 'discoveries',
            folderId: job.folderId,
            progress: progress as DiscoveryProgress | null,
          }
        : { kind: 'scans', folderId: job.folderId, progress: progress as ScanProgress | null }

  return (
    <FolderJobsContext.Provider value={{ activeJob, refreshFolder, scanFolder }}>
      {children}
    </FolderJobsContext.Provider>
  )
}

export function useFolderJobs(): FolderJobs {
  const value = useContext(FolderJobsContext)
  if (value === null) throw new Error('useFolderJobs must be used inside FolderJobsProvider')
  return value
}
