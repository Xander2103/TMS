import { PageHeader } from '../layout/PageHeader'

export function NotFoundPage() {
  return (
    <>
      <PageHeader title="Pagina niet gevonden" />
      <p className="placeholder-text">De opgevraagde pagina bestaat niet.</p>
    </>
  )
}
