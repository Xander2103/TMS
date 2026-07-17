import './TransportStatusBadge.css'

interface TransportStatusBadgeProps {
  status: string
}

const STATUS_CLASS_MAP: Record<string, string> = {
  Gepland: 'status-planned',
  Onderweg: 'status-in-transit',
  Afgeleverd: 'status-delivered',
}

export function TransportStatusBadge({ status }: TransportStatusBadgeProps) {
  const statusClass = STATUS_CLASS_MAP[status] ?? 'status-default'

  return <span className={`status-badge ${statusClass}`}>{status}</span>
}
