import { useEffect, useRef, useState } from 'react'
import { apiFetch } from './client'
import type {
  ActiveJobDto,
  DiscoveryProgress,
  FaceRecognitionProgress,
  JobStatus,
  ScanProgress,
} from './types'

const TERMINAL: ReadonlySet<JobStatus> = new Set(['Completed', 'Failed', 'Cancelled'])

/** The single discovery/scan job the backend has running right now, if any. */
export function getActiveJob(): Promise<ActiveJobDto | null> {
  return apiFetch<ActiveJobDto | undefined>('/api/jobs/active').then((job) => job ?? null)
}

export function startDiscovery(folderId: number): Promise<number> {
  return apiFetch<{ discoveryJobId: number }>('/api/discoveries', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ folderId }),
  }).then((response) => response.discoveryJobId)
}

export function startScan(folderId: number, isRecursive: boolean): Promise<number> {
  return apiFetch<{ scanJobId: number }>('/api/scans', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ folderId, isRecursive }),
  }).then((response) => response.scanJobId)
}

/** reanalyze: forget that the photos in scope were analysed, so every one is analysed again. */
export function startFaceRecognition(
  folderId: number | null,
  isRecursive: boolean,
  reanalyze = false,
): Promise<number> {
  return apiFetch<{ faceRecognitionJobId: number }>('/api/face-recognitions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      ...(folderId === null ? {} : { folderId }),
      isRecursive,
      ...(reanalyze && { reanalyze }),
    }),
  }).then((response) => response.faceRecognitionJobId)
}

/** Asks the backend to cancel a running face recognition job; it reports Cancelled on its event stream. */
export function cancelJob(id: number): Promise<void> {
  return apiFetch<void>(`/api/jobs/${id}/cancel`, { method: 'POST' })
}

type RawDiscoveryEvent = {
  Id: number
  Status: JobStatus
  FoldersDiscovered: number
  ErrorMessage: string | null
}
type RawScanEvent = {
  Id: number
  Status: JobStatus
  FoldersScanned: number
  FilesFound: number
  FilesEnriched: number
  ErrorMessage: string | null
}

type RawFaceEvent = {
  Id: number
  Status: JobStatus
  ImagesFound: number
  ImagesProcessed: number
  FacesFound: number
  ErrorMessage: string | null
}

// The event stream is hand-serialized on the server (JsonSerializer.Serialize with no options), so
// unlike the rest of the API it comes back PascalCase; map it onto the app's camelCase types here.
function toDiscoveryProgress(raw: RawDiscoveryEvent): DiscoveryProgress {
  return {
    id: raw.Id,
    status: raw.Status,
    foldersDiscovered: raw.FoldersDiscovered,
    errorMessage: raw.ErrorMessage,
  }
}

function toScanProgress(raw: RawScanEvent): ScanProgress {
  return {
    id: raw.Id,
    status: raw.Status,
    foldersScanned: raw.FoldersScanned,
    filesFound: raw.FilesFound,
    filesEnriched: raw.FilesEnriched,
    errorMessage: raw.ErrorMessage,
  }
}

function toFaceProgress(raw: RawFaceEvent): FaceRecognitionProgress {
  return {
    id: raw.Id,
    status: raw.Status,
    imagesFound: raw.ImagesFound,
    imagesProcessed: raw.ImagesProcessed,
    facesFound: raw.FacesFound,
    errorMessage: raw.ErrorMessage,
  }
}

export type JobKind = 'discoveries' | 'scans' | 'face-recognitions'

type Progress = DiscoveryProgress | ScanProgress | FaceRecognitionProgress

/**
 * Follows a discovery/scan job's SSE stream, returning its latest progress until a terminal
 * status. `onEvent` (fired for every message, including the terminal one) is read from a ref so
 * passing a fresh callback each render doesn't resubscribe.
 */
export function useJobEvents(
  kind: JobKind,
  jobId: number | null,
  onEvent?: (progress: Progress) => void,
): Progress | null {
  const [trackedJobId, setTrackedJobId] = useState(jobId)
  const [progress, setProgress] = useState<Progress | null>(null)
  const onEventRef = useRef(onEvent)
  useEffect(() => {
    onEventRef.current = onEvent
  })

  // Adjusting state during render (not in an effect) when the job changes, per
  // https://react.dev/learn/you-might-not-need-an-effect#adjusting-some-state-when-a-prop-changes
  if (jobId !== trackedJobId) {
    setTrackedJobId(jobId)
    setProgress(null)
  }

  useEffect(() => {
    if (jobId === null) return

    const controller = new AbortController()

    async function read() {
      const response = await fetch(
        new URL(`/api/${kind}/${jobId}/events`, window.location.origin),
        {
          signal: controller.signal,
          headers: { Accept: 'text/event-stream' },
        },
      )
      const reader = response.body?.getReader()
      if (!reader) return

      const decoder = new TextDecoder()
      let buffer = ''

      for (;;) {
        const { done, value } = await reader.read()
        if (done) return
        buffer += decoder.decode(value, { stream: true })

        let separator: number
        while ((separator = buffer.indexOf('\n\n')) !== -1) {
          const chunk = buffer.slice(0, separator)
          buffer = buffer.slice(separator + 2)
          const dataLine = chunk.split('\n').find((line) => line.startsWith('data: '))
          if (!dataLine) continue

          const raw = JSON.parse(dataLine.slice('data: '.length)) as
            RawDiscoveryEvent | RawScanEvent | RawFaceEvent
          const event =
            kind === 'discoveries'
              ? toDiscoveryProgress(raw as RawDiscoveryEvent)
              : kind === 'scans'
                ? toScanProgress(raw as RawScanEvent)
                : toFaceProgress(raw as RawFaceEvent)
          setProgress(event)
          onEventRef.current?.(event)
          if (TERMINAL.has(event.status)) return
          // Yields a macrotask so each intermediate state actually renders, even when several
          // messages arrive in the same read() (e.g. buffered close to each other).
          await new Promise((resolve) => setTimeout(resolve, 0))
        }
      }
    }

    read().catch(() => {
      // AbortError on cleanup is expected; any other failure just leaves progress as last known.
    })

    return () => controller.abort()
  }, [kind, jobId])

  return progress
}
