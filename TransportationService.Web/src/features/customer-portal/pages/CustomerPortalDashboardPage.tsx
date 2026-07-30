import { PageHeader } from '../../../components/layout/PageHeader'
import { useAuth } from '../../auth/authContextValue'

/** Portal landing page. Full dashboard content is a later phase — this simply orients the
 * user and links to what already works today (Opdrachten). */
export function CustomerPortalDashboardPage() {
  const { user } = useAuth()

  return (
    <div>
      <PageHeader title={`Welkom${user?.firstName ? `, ${user.firstName}` : ''}`} subtitle="Klantportaal" />
      <p>
        Vanuit dit portaal kunt u uw transportopdrachten opvolgen en indienen. Meer functionaliteit
        (documenten, facturen en berichten) volgt binnenkort.
      </p>
    </div>
  )
}
