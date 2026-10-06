/** History state that marks the viewer as opened from the Suggested strip, so it steps through the suggestions. */
export function openedFromStrip(state: unknown): boolean {
  return typeof state === 'object' && state !== null && 'list' in state && state.list === 'suggested'
}
