import { http, HttpResponse } from 'msw'

// One event per pull(), a tick apart, like the real backend's polling loop -- letting each
// intermediate state actually render, instead of dumping every event into one buffered chunk.
function sseStream(events: Array<Record<string, unknown>>): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder()
  let index = 0
  return new ReadableStream({
    async pull(controller) {
      if (index >= events.length) {
        controller.close()
        return
      }
      await new Promise((resolve) => setTimeout(resolve, 10))
      controller.enqueue(encoder.encode(`data: ${JSON.stringify(events[index])}\n\n`))
      index++
    },
  })
}

/** Overrides the discovery job `id`'s event stream with `events`, sent one SSE message per item. */
export function discoveryEvents(id: number, events: Array<Record<string, unknown>>) {
  return http.get(
    `/api/discoveries/${id}/events`,
    () => new HttpResponse(sseStream(events), { headers: { 'Content-Type': 'text/event-stream' } }),
  )
}

/** Overrides the scan job `id`'s event stream with `events`, sent one SSE message per item. */
export function scanEvents(id: number, events: Array<Record<string, unknown>>) {
  return http.get(
    `/api/scans/${id}/events`,
    () => new HttpResponse(sseStream(events), { headers: { 'Content-Type': 'text/event-stream' } }),
  )
}

/**
 * A discovery/scan event stream the test drives by hand: `send` only resolves once the message
 * has actually been written to the response body, so a test can assert on one transient progress
 * state before pushing the next -- no fixed delay to race against machine load.
 */
export function controllableEvents(path: string, id: number) {
  let controller: ReadableStreamDefaultController<Uint8Array> | undefined
  const stream = new ReadableStream<Uint8Array>({
    start(c) {
      controller = c
    },
  })
  const encoder = new TextEncoder()
  const handler = http.get(
    `${path}/${id}/events`,
    () => new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream' } }),
  )
  return {
    handler,
    send(event: Record<string, unknown>) {
      controller!.enqueue(encoder.encode(`data: ${JSON.stringify(event)}\n\n`))
    },
    close() {
      controller!.close()
    },
  }
}

/** Baseline: every discovery/scan starts as job 1 and immediately completes; override per test as needed. */
export const jobHandlers = [
  http.post('/api/discoveries', () => HttpResponse.json({ discoveryJobId: 1 })),
  http.post('/api/scans', () => HttpResponse.json({ scanJobId: 1 })),
  discoveryEvents(1, [{ Id: 1, Status: 'Completed', FoldersDiscovered: 0, ErrorMessage: null }]),
  scanEvents(1, [
    {
      Id: 1,
      Status: 'Completed',
      FoldersScanned: 0,
      FilesFound: 0,
      FilesEnriched: 0,
      ErrorMessage: null,
    },
  ]),
]
