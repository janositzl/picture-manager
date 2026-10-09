import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { useCurrentUser } from '../api/auth'
import {
  cancelJob,
  getActiveJob,
  startDiscovery,
  startFaceRecognition,
  startScan,
  useJobEvents,
  type FacePreset,
  type JobKind,
} from '../api/jobs'
import { ApiError } from '../api/client'
import { queryKeys } from '../api/queries'
import type { DiscoveryProgress, FaceRecognitionProgress, ScanProgress } from '../api/types'
import { useNotify } from '../app/notify'

export type ActiveJob =
  | {
      kind: 'discoveries'
      folderId: number | null
      jobId: number
      progress: DiscoveryProgress | null
    }
  | { kind: 'scans'; folderId: number | null; jobId: number; progress: ScanProgress | null }
  | {
      kind: 'face-recognitions'
      folderId: number | null
      jobId: number
      progress: FaceRecognitionProgress | null
    }

type FolderJobs = {
  activeJob: ActiveJob | null
  refreshFolder: (folderId: number) => void
  scanFolder: (folderId: number, isRecursive: boolean) => void
  /** Recursive by default; reanalyze runs the detector again on photos that were already analysed. */
  recognizeFaces: (
    folderId: number | null,
    options?: { isRecursive?: boolean; reanalyze?: boolean; preset?: FacePreset },
  ) => void
  cancelActiveJob: () => void
}

const FolderJobsContext = createContext<FolderJobs | null>(null)

function startErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) return 'Another job is already in progress.'
    const fieldError = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (fieldError) return fieldError
  }
  return "Couldn't start the job."
}

/** Tracks the single discovery/scan job the backend allows at a time, shared by the whole tree. */
export function FolderJobsProvider({ children }: { children: ReactNode }) {
  const [job, setJob] = useState<{ kind: JobKind; folderId: number | null; jobId: number } | null>(
    null,
  )
  const queryClient = useQueryClient()
  const notify = useNotify()

  // Restores a job that's still running server-side after a page load, so a reload mid-scan
  // doesn't lose track of it (the backend is the source of truth, not this component's state).
  // folderId is null for a whole-instance job (e.g. the startup catch-up re-enriching images an
  // interrupted scan left unenriched, or a scan/discovery of every active root); it still needs to
  // be tracked so the banner shows it and HasActiveJobAsync's 409 isn't a silent surprise.
  const signedInId = useCurrentUser().data?.id ?? null
  useEffect(() => {
    if (signedInId === null) return
    let cancelled = false
    getActiveJob()
      .then((active) => {
        if (cancelled || active === null) return
        const kind: JobKind =
          active.kind === 'Discovery'
            ? 'discoveries'
            : active.kind === 'Scan'
              ? 'scans'
              : 'face-recognitions'
        setJob({ kind, folderId: active.folderId, jobId: active.id })
      })
      .catch(() => {
        // No harm leaving the UI unaware of an active job it couldn't confirm; it'll surface via a 409 if the user tries to start one.
      })
    return () => {
      cancelled = true
    }
  }, [signedInId])

  const progress = useJobEvents(job?.kind ?? 'discoveries', job?.jobId ?? null, (event) => {
    if (event.status === 'Failed') {
      notify(event.errorMessage ?? 'The job failed.')
      setJob(null)
    } else if (event.status === 'Completed' || event.status === 'Cancelled') {
      void queryClient.invalidateQueries({ queryKey: ['folders'] })
      void queryClient.invalidateQueries({ queryKey: ['people'] })
      // A scan can add, remove or re-enrich images; every grid (and the viewer walking the same
      // cached pages) needs to see that, not just the folder tree's counts.
      void queryClient.invalidateQueries({ queryKey: queryKeys.imageLists() })
      setJob(null)
    }
  })

  const run = useCallback(
    (kind: JobKind, folderId: number | null, action: () => Promise<number>) => {
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

  const recognizeFaces = useCallback(
    (
      folderId: number | null,
      {
        isRecursive = true,
        reanalyze = false,
        preset = 'fast',
      }: { isRecursive?: boolean; reanalyze?: boolean; preset?: FacePreset } = {},
    ) =>
      run('face-recognitions', folderId, () =>
        startFaceRecognition(folderId, isRecursive, reanalyze, preset),
      ),
    [run],
  )
  const cancelActiveJob = useCallback(() => {
    if (job === null) return
    cancelJob(job.jobId).catch(() => notify("Couldn't cancel the job."))
  }, [job, notify])

  function buildActiveJob(): ActiveJob | null {
    if (job === null) return null
    switch (job.kind) {
      case 'discoveries':
        return {
          kind: 'discoveries',
          folderId: job.folderId,
          jobId: job.jobId,
          progress: progress as DiscoveryProgress | null,
        }
      case 'scans':
        return {
          kind: 'scans',
          folderId: job.folderId,
          jobId: job.jobId,
          progress: progress as ScanProgress | null,
        }
      case 'face-recognitions':
        return {
          kind: 'face-recognitions',
          folderId: job.folderId,
          jobId: job.jobId,
          progress: progress as FaceRecognitionProgress | null,
        }
    }
  }
  const activeJob = buildActiveJob()

  return (
    <FolderJobsContext.Provider
      value={{ activeJob, refreshFolder, scanFolder, recognizeFaces, cancelActiveJob }}
    >
      {children}
    </FolderJobsContext.Provider>
  )
}

export function useFolderJobs(): FolderJobs {
  const value = useContext(FolderJobsContext)
  if (value === null) throw new Error('useFolderJobs must be used inside FolderJobsProvider')
  return value
}
