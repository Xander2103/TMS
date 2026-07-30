import { PageHeader } from '../../../components/layout/PageHeader'
import { EmptyState } from '../../../components/ui/EmptyState'

/**
 * Placeholder pages for the Phase 9 portal modules. The backend endpoints for these don't exist
 * yet, so there is nothing real to wire up — these deliberately only orient the user, per the
 * phase-8 brief ("Dashboard placeholder listing 'binnenkort'... ONLY where the existing portal
 * endpoints have no data yet").
 */
function ComingSoon({ title }: { title: string }) {
  return (
    <div>
      <PageHeader title={title} subtitle="Klantportaal" />
      <EmptyState message="Deze functionaliteit komt binnenkort beschikbaar." />
    </div>
  )
}

export function CustomerPortalDocumentsPage() {
  return <ComingSoon title="Documenten" />
}

export function CustomerPortalInvoicesPage() {
  return <ComingSoon title="Facturen" />
}

export function CustomerPortalMessagesPage() {
  return <ComingSoon title="Berichten" />
}
