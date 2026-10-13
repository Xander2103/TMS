# Voorstel: partnertypes (klant / leverancier / onderaannemer / interne firma)

Status: voorstel (fase 12 van de verbeteringsgolf 2026-07-20) — nog niet geïmplementeerd.

## Probleem

`Customer` is vandaag het enige partnermodel. In de praktijk zijn relaties breder:
leveranciers, onderaannemers (charters), depots/agenten en interne firma's binnen een
groep. Vandaag wordt dit deels opgevangen met `CustomerCategory`-lookups ("Partner",
"Onderaannemer", "Leverancier", "Interne firma"), maar een categorie geeft geen ander
gedrag: elke rij krijgt een KL-nummer, telt mee in klantstatistieken en kan als
opdrachtgever van een order worden gekozen.

## Voorgestelde richting: één Party, meerdere rollen

Eén `Party`-tabel (naam, adressen, BTW/ondernemingsnummer, contactgegevens, bankgegevens)
met een set **rollen** per partij in een aparte tabel `PartyRole`
(`Customer | Supplier | Subcontractor | InternalCompany`), elk met roleigen velden:

- `CustomerRole`: klantnummer, betalingscondities, kredietlimiet/blokkering, tarievenkaarten.
- `SupplierRole`: leveranciersnummer, inkoopvoorwaarden, IBAN-verificatiestatus.
- `SubcontractorRole`: charter-voorwaarden, verzekeringsdocumenten, vlootcapaciteit,
  koppelbaar aan ritten als uitvoerder.
- `InternalCompanyRole`: koppeling naar de eigen tenant/facturatie-entiteit voor
  intercompany-verkeer.

Eén partij kan meerdere rollen tegelijk hebben (een onderaannemer die ook klant is) zonder
dubbele invoer van adres- en BTW-gegevens — het klassieke party-role patroon.

## Migratiepad (niet-brekend, in drie stappen)

1. **Additief**: introduceer `Party` + `PartyRole`; elke bestaande `Customer` krijgt
   automatisch een `Party` + `CustomerRole` (backfill-migratie, `Customer.PartyId` FK).
   Bestaande code blijft tegen `Customer` praten.
2. **Nieuwe rollen**: leveranciers/onderaannemers worden als nieuwe rollen op bestaande of
   nieuwe parties aangemaakt; nieuwe modules (charteropdrachten, inkoopfacturen) verwijzen
   naar de rol, nooit naar `Customer`.
3. **Afbouw**: zodra alle lees-/schrijfpaden via de rollen lopen, wordt `Customer` een
   view/alias op `Party`+`CustomerRole`. Kolommen worden pas verwijderd nadat elke consumer
   is gemigreerd (aparte, latere migratie — nooit destructief in stap 1/2).

## Permissies en nummering

- Per rol een eigen nummerreeks in `TenantSettings` (KL- blijft; LEV-, OA-, INT- komen bij)
  via het bestaande `TenantNumbering`-mechanisme.
- Permissies per rol-resource (`suppliers.view/manage`, `subcontractors.view/manage`), zodat
  boekhouding leveranciers kan beheren zonder klantadministratierechten.

## Waarom niet gewoon meer categorieën?

Categorieën zijn presentatie, geen gedrag: ze geven geen aparte nummerreeksen, geen
roleigen velden, geen aparte permissies en geen veilige manier om een partij tegelijk
klant én onderaannemer te laten zijn. Het party-role model lost dat structureel op en is
in stap 1 volledig additief.

## Geschatte omvang

Stap 1 (Party + backfill + CustomerRole, geen UI-wijziging): 2-3 dagen.
Stap 2 per nieuwe rol (entiteit + service + permissies + minimale UI): 2-4 dagen per rol.
