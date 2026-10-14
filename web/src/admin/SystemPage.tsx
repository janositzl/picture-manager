import RefreshIcon from '@mui/icons-material/Refresh'
import { Box, Button, Chip, FormControlLabel, Paper, Switch, Typography } from '@mui/material'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useSystemInfo, useSystemLogs } from '../api/queries'
import type { AppVersionInfo, LogEntry, LogSeverity } from '../api/types'
import { HEADING_SX } from '../design/accent'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'

type Tone = { border: string; background: string; chip: 'default' | 'warning' | 'error' | 'info' }

const TONES: Record<LogSeverity, Tone> = {
  Verbose: { border: 'transparent', background: 'transparent', chip: 'default' },
  Debug: { border: 'transparent', background: 'transparent', chip: 'default' },
  Information: { border: 'transparent', background: 'transparent', chip: 'info' },
  Warning: { border: '#f59e0b', background: 'rgba(245, 158, 11, 0.10)', chip: 'warning' },
  Error: { border: '#dc2626', background: 'rgba(220, 38, 38, 0.10)', chip: 'error' },
  Fatal: { border: '#dc2626', background: 'rgba(220, 38, 38, 0.18)', chip: 'error' },
}
const NEUTRAL_TONE: Tone = { border: 'transparent', background: 'transparent', chip: 'default' }

const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })

function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso))
}

function formatUptime(startedIso: string, now: number): string {
  const minutes = Math.max(0, Math.floor((now - new Date(startedIso).getTime()) / 60_000))
  const days = Math.floor(minutes / 1440)
  const hours = Math.floor((minutes % 1440) / 60)
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${minutes % 60}m`
  return `${minutes}m`
}

function isProblem(level: LogSeverity | null): boolean {
  return level === 'Error' || level === 'Fatal'
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="body2">{value}</Typography>
    </Box>
  )
}

function VersionCard({ info }: { info: AppVersionInfo }) {
  // Captured once per mount: the uptime is a rough figure and must not make render impure.
  const [now] = useState(() => Date.now())
  return (
    <Paper variant="outlined" sx={{ p: 2.5, borderRadius: '12px' }}>
      <Typography variant="caption" color="text.secondary">
        Deployed version
      </Typography>
      <Typography variant="h4" component="p" sx={{ ...HEADING_SX, mb: 2 }}>
        {info.version}
      </Typography>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
        <Fact label="Built" value={formatDateTime(info.buildTimeUtc)} />
        <Fact
          label="Running since"
          value={`${formatDateTime(info.startedUtc)} (up ${formatUptime(info.startedUtc, now)})`}
        />
        <Fact label="Environment" value={info.environment} />
        <Fact label="Runtime" value={info.runtime} />
      </Box>
    </Paper>
  )
}

function LogRow({ entry }: { entry: LogEntry }) {
  const tone = entry.level === null ? NEUTRAL_TONE : TONES[entry.level]
  return (
    <Box
      data-testid="log-entry"
      data-level={entry.level ?? 'none'}
      sx={{
        borderLeft: 3,
        borderColor: tone.border,
        bgcolor: tone.background,
        px: 1.5,
        py: 0.5,
        fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
        fontSize: 12.5,
      }}
    >
      <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'baseline' }}>
        <Box component="span" sx={{ color: 'text.secondary', whiteSpace: 'nowrap' }}>
          {entry.timestamp?.slice(0, 19) ?? ''}
        </Box>
        {entry.level !== null && (
          <Chip label={entry.level} size="small" color={tone.chip} variant="outlined" sx={{ height: 18, fontSize: 11 }} />
        )}
        <Box component="span" sx={{ wordBreak: 'break-word', fontWeight: isProblem(entry.level) ? 600 : 400 }}>
          {entry.message}
        </Box>
      </Box>
      {entry.exception !== null && (
        <details open={isProblem(entry.level)}>
          <summary style={{ cursor: 'pointer', color: 'inherit', opacity: 0.7 }}>Details</summary>
          <Box component="pre" sx={{ m: 0, mt: 0.5, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
            {entry.exception}
          </Box>
        </details>
      )}
    </Box>
  )
}

function LogSection() {
  const [warningsOnly, setWarningsOnly] = useState(false)
  const logs = useSystemLogs(warningsOnly)
  const listRef = useRef<HTMLDivElement>(null)

  // Newest entries are at the bottom: show them after each load.
  useEffect(() => {
    const list = listRef.current
    if (list !== null) list.scrollTop = list.scrollHeight
  }, [logs.data])

  let body: ReactNode
  if (logs.isPending) {
    body = null
  } else if (logs.isError) {
    body = <QueryErrorAlert message="Couldn't load the log." onRetry={() => void logs.refetch()} />
  } else if (logs.data.fileName === null) {
    body = <Typography color="text.secondary">No log file found on the server.</Typography>
  } else if (logs.data.entries.length === 0) {
    body = <Typography color="text.secondary">No matching log entries.</Typography>
  } else {
    body = (
      <Paper variant="outlined" sx={{ borderRadius: '12px', overflow: 'hidden' }}>
        <Box ref={listRef} sx={{ maxHeight: '55vh', overflowY: 'auto' }}>
          {logs.data.entries.map((entry, index) => (
            <LogRow key={index} entry={entry} />
          ))}
        </Box>
      </Paper>
    )
  }

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 1.5, flexWrap: 'wrap' }}>
        <Typography variant="h6" component="h2" sx={HEADING_SX}>
          Recent log
        </Typography>
        {logs.data?.fileName != null && (
          <Typography variant="caption" color="text.secondary">
            {logs.data.fileName}
            {logs.data.truncated ? ' · showing the latest entries only' : ''}
          </Typography>
        )}
        <Box sx={{ flex: 1 }} />
        <FormControlLabel
          control={
            <Switch checked={warningsOnly} onChange={(event) => setWarningsOnly(event.target.checked)} />
          }
          label="Warnings & errors only"
        />
        <Button
          size="small"
          startIcon={<RefreshIcon />}
          disabled={logs.isFetching}
          onClick={() => void logs.refetch()}
        >
          Refresh
        </Button>
      </Box>
      {body}
    </Box>
  )
}

export function SystemPage() {
  const info = useSystemInfo()

  return (
    <Box sx={{ p: 3, maxWidth: 1000, display: 'flex', flexDirection: 'column', gap: 3 }}>
      {info.isError ? (
        <QueryErrorAlert message="Couldn't load version information." onRetry={() => void info.refetch()} />
      ) : (
        info.data !== undefined && <VersionCard info={info.data} />
      )}
      <LogSection />
    </Box>
  )
}
