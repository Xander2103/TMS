import { Link } from 'react-router-dom'
import { RECIPIENT_TYPE_LABELS } from '../types'

/**
 * Purely informational tab: explains how each recipient type resolves at send time, and points
 * to where per-customer communication routing actually lives. No backend of its own — the
 * routing logic is documented here, not duplicated.
 */
export function RecipientsInfoTab() {
  return (
    <div className="notification-admin-info">
      <h3>Hoe ontvangers worden bepaald</h3>
      <p>
        Elke gebeurtenis in het tabblad <strong>Gebeurtenissen</strong> heeft één of meer ontvangertypes. Bij het
        versturen wordt elk type opgelost naar concrete e-mailadressen of in-app-meldingen:
      </p>
      <dl className="notification-admin-recipient-glossary">
        <dt>{RECIPIENT_TYPE_LABELS.CustomerPrimaryContact}</dt>
        <dd>Het actieve hoofdcontact van de klant op de order/factuur; zonder hoofdcontact valt dit terug op het algemene e-mailadres van de klant.</dd>

        <dt>{RECIPIENT_TYPE_LABELS.CustomerCommunicationRule}</dt>
        <dd>
          Gebruikt de communicatieregels die per klant zijn ingesteld (welk contact welk type bericht krijgt). Beheer
          je op de klantfiche zelf — zie de link hieronder.
        </dd>

        <dt>{RECIPIENT_TYPE_LABELS.InternalPermission}</dt>
        <dd>Elke actieve gebruiker die het gekozen recht heeft, ongeacht in welke rol dat recht zit.</dd>

        <dt>{RECIPIENT_TYPE_LABELS.InternalRole}</dt>
        <dd>Elke actieve gebruiker in een rol met de opgegeven sjabloon-rolcode.</dd>

        <dt>{RECIPIENT_TYPE_LABELS.ExplicitEmail}</dt>
        <dd>Eén vast e-mailadres, los van gebruikers of klanten (bv. een gedeelde mailbox).</dd>

        <dt>{RECIPIENT_TYPE_LABELS.Driver}</dt>
        <dd>De chauffeur van de opdracht, of — bij HR-gebeurtenissen — de betrokken medewerker zelf.</dd>
      </dl>
      <p>
        Per-klant communicatieregels (welk klantcontact welk type e-mail krijgt) beheer je op de klantfiche, tabblad{' '}
        <em>Communicatie</em>.
      </p>
      <Link to="/customers" className="notification-admin-link-button">
        Naar klanten →
      </Link>
    </div>
  )
}
