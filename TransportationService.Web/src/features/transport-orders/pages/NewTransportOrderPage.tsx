import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { useToast } from '../../../components/ui/toastContext'
import { createTransportOrder, getTransportOrder } from '../api/transportOrdersApi'
import { TransportOrderForm } from '../components/TransportOrderForm'
import type { TransportOrderDetail } from '../types'

export function NewTransportOrderPage() {
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const [searchParams] = useSearchParams()
  const templateId = searchParams.get('template')

  // Template mode: prefill the form with an existing order; the result is still a brand-new
  // draft (new number, new stops) — nothing links back to the source order. Loading is
  // derived from a request key so no setState runs synchronously in the effect.
  const [loadedTemplate, setLoadedTemplate] = useState<{ id: string; order: TransportOrderDetail | null } | null>(null)
  const template = templateId && loadedTemplate?.id === templateId ? loadedTemplate.order : null
  const templateLoading = Boolean(templateId) && loadedTemplate?.id !== templateId

  useEffect(() => {
    if (!templateId) return
    let mounted = true
    getTransportOrder(templateId)
      .then((data) => {
        if (mounted) setLoadedTemplate({ id: templateId, order: data })
      })
      .catch(() => {
        if (!mounted) return
        showError('De sjabloonopdracht kon niet worden geladen; het formulier start leeg.')
        setLoadedTemplate({ id: templateId, order: null })
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [templateId])

  if (templateLoading) {
    return <LoadingState message="Sjabloon laden..." />
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Transportopdrachten', to: '/transport-orders' }, { label: 'Nieuwe opdracht' }]} />
      <BackButton to="/transport-orders" label="Terug naar opdrachten" />
      <PageHeader
        title="Nieuwe transportopdracht"
        subtitle={
          template
            ? `Vooraf ingevuld op basis van ${template.orderNumber}; pas aan waar nodig.`
            : 'De opdracht start als concept en kan daarna worden bevestigd.'
        }
      />
      <TransportOrderForm
        key={template?.id ?? 'blank'}
        order={template ?? undefined}
        submitLabel="Opdracht aanmaken"
        onCancel={() => navigate('/transport-orders')}
        onSubmit={async (input) => {
          const created = await createTransportOrder(input)
          showSuccess(`Opdracht ${created.orderNumber} aangemaakt.`)
          navigate(`/transport-orders/${created.id}`)
        }}
      />
    </div>
  )
}
