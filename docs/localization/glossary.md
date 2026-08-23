# Domeinglossary NL / FR / EN

**Dé canonieke terminologiereferentie** (i18n-wave §22/§69/§88). Elke vertaling in
resources, e-mails, PDF's en exports volgt deze tabel; afwijken = eerst deze tabel
aanpassen. FR = professioneel Belgisch transport-/HR-taalgebruik, geen letterlijke
woord-voor-woordvertalingen. Bij ambigue termen staat de context erbij.

## Transport & logistiek

| Key | NL | FR | EN | Context/notities |
|---|---|---|---|---|
| transportOrder | Transportopdracht | Commande de transport | Transport order | Het commerciële werkobject ("opdracht"). Niet "ordre de transport". |
| dossier | Dossier | Dossier | File | Overkoepelend werkdossier; EN "file" (UI), nooit "dossier" tenzij juridisch. |
| trip | Rit | Tournée | Trip | Eén geplande voertuigronde met stops. FR "tournée" (rondrit-context), niet "voyage"/"trajet". |
| route | Route | Itinéraire | Route | Het gereden traject binnen een rit. |
| stop | Stop | Arrêt | Stop | Laad-/losplaats binnen een rit. |
| loadingAddress | Laadadres | Adresse de chargement | Loading address | |
| deliveryAddress | Losadres | Adresse de livraison | Delivery address | NL "losadres"; FR bewust "livraison" (klantperspectief), niet "déchargement". |
| loading | Laden | Chargement | Loading | |
| unloading | Lossen | Déchargement | Unloading | Operationeel magazijnperspectief. |
| trailer | Oplegger | Semi-remorque | Trailer | Nooit FR "remorque" (= aanhangwagen). |
| vehicle | Voertuig | Véhicule | Vehicle | Trekker/bakwagen: "trekker" = "tracteur" = "tractor unit". |
| loadingMetre | Laadmeter | Mètre de plancher | Loading metre | Vloerlengte-eenheid (LDM). FR-praktijk: "mètre de plancher"; afkorting LDM mag overal. |
| pallet | Pallet | Palette | Pallet | |
| package / collo | Collo (mv. colli) | Colis | Package | Barcode-eenheid in de custody chain. |
| shipment | Zending | Envoi | Shipment | |
| driver | Chauffeur | Chauffeur | Driver | FR "chauffeur" is de Belgische beroepsterm (niet "conducteur"). |
| dispatcher | Dispatcher | Dispatcher | Dispatcher | Ingeburgerde vakterm in alle drie de talen. |
| planner | Planner | Planificateur | Planner | |
| warehouse | Magazijn | Entrepôt | Warehouse | |
| dock | Dock | Quai | Dock | Laadkade. |
| POD | Leveringsbewijs (POD) | Preuve de livraison (POD) | Proof of delivery (POD) | Afkorting POD overal toegestaan. |
| CMR | CMR-vrachtbrief | Lettre de voiture CMR | CMR consignment note | |
| timeWindow | Tijdvenster | Fenêtre horaire | Time window | |
| emptyKilometres | Lege kilometers | Kilomètres à vide | Empty kilometres | |
| dieselSurcharge | Dieseltoeslag | Surcharge gazole | Diesel surcharge | |

## HR & personeel

| Key | NL | FR | EN | Context/notities |
|---|---|---|---|---|
| employee | Medewerker | Collaborateur | Employee | FR "collaborateur" (modern-zakelijk BE), niet "employé" in UI-labels. |
| personnelNumber | Personeelsnummer | Numéro de personnel | Employee number | |
| department | Afdeling | Département | Department | |
| jobFunction | Functie | Fonction | Job function | |
| contractType | Contracttype | Type de contrat | Contract type | Waardes (bv. FixedTerm) zijn tenant-lookupdata — labels blijven in de databank. |
| absence | Afwezigheid | Absence | Absence | |
| leave | Verlof | Congé | Leave | |
| leaveBalance | Verlofsaldo | Solde de congés | Leave balance | |
| sickness | Ziekte | Maladie | Sickness | |
| qualification | Kwalificatie | Qualification | Qualification | Rijbewijs/code95/ADR-documenten. |
| issuedItem | Bedrijfsmiddel | Matériel d'entreprise | Company asset | Uitgereikt materiaal (telefoon, kledij…). |
| fuelCard | Tankkaart | Carte carburant | Fuel card | |

## Attendance & tijd (incl. tachograafcontext, §88)

| Key | NL | FR | EN | Context/notities |
|---|---|---|---|---|
| workStatus | Werkstatus | Statut de travail | Work status | |
| clockIn | Inpunten | Pointer l'entrée | Clock in | Belgisch "inpunten"; FR "pointer" is de vakterm. |
| clockOut | Uitpunten | Pointer la sortie | Clock out | |
| startBreak | Pauze starten | Commencer la pause | Start break | |
| endBreak | Pauze beëindigen | Terminer la pause | End break | |
| notClockedIn | Niet ingepunt | Non pointé | Not clocked in | |
| working | Aan het werk | Au travail | Working | |
| onBreak | Pauze | En pause | On break | |
| myHours | Mijn uren | Mes heures | My hours | |
| workedToday | Vandaag gewerkt | Temps travaillé aujourd'hui | Worked today | |
| timeClock / kiosk | Prikklok | Pointeuse | Time clock | Het fysieke toestel; "kiosk" alleen technisch (route /kiosk). |
| timeRegistration | Urenregistratie | Enregistrement des heures | Time registration | Modulenaam. |
| correction | Correctie | Correction | Correction | Altijd met verplichte reden/motif/reason. |
| driving | Rijden | Conduite | Driving | Tachograaf-activiteit — komt UITSLUITEND uit een tachograafbron. |
| otherWork | Andere arbeid | Autres tâches | Other work | Tachograafterm (EU 561/2006-context). |
| availability | Beschikbaarheid | Disponibilité | Availability | Tachograafterm. |
| rest | Rust | Repos | Rest | Tachograafterm; ≠ pauze (pause/break) in attendance. |
| dutyTime | Diensttijd | Temps de service | Duty time | |
| tachograph | Tachograaf | Tachygraphe | Tachograph | |

## Planning

| Key | NL | FR | EN | Context/notities |
|---|---|---|---|---|
| shift | Shift | Poste | Shift | Geplande personeelsdienst. |
| planned | Gepland | Prévu | Planned | Verwacht (planning) vs. werkelijk (attendance). |
| actual | Werkelijk | Réel | Actual | |
| deviation | Afwijking | Écart | Deviation | Netto − gepland. |
| planningBoard | Planbord | Tableau de planification | Planning board | |

## Pricing & facturatie

| Key | NL | FR | EN | Context/notities |
|---|---|---|---|---|
| rate / tariff | Tarief | Tarif | Rate | |
| rateTable | Tarieventabel | Grille tarifaire | Rate table | |
| agreement | Prijsafspraak | Accord tarifaire | Price agreement | |
| surcharge | Toeslag | Supplément | Surcharge | |
| invoice | Factuur | Facture | Invoice | |
| creditNote | Creditnota | Note de crédit | Credit note | |
| vat | Btw | TVA | VAT | |
| quote | Offerte | Offre | Quote | |

## Voorraad & magazijn

| Key | NL | FR | EN | Context/notities |
|---|---|---|---|---|
| inventory / stock | Voorraad | Stock | Inventory | |
| stockCount | Telling | Inventaire | Stock count | |
| lowStock | Lage voorraad | Stock bas | Low stock | |
| unitType | Eenheid | Unité | Unit | |

## Systeembeheer

| Key | NL | FR | EN | Context/notities |
|---|---|---|---|---|
| settings | Instellingen | Paramètres | Settings | |
| backup | Back-up | Sauvegarde | Backup | Server-gegenereerde bestandsnamen blijven onvertaald (§92). |
| restore | Terugzetten | Restaurer | Restore | |
| auditLog | Auditlog | Journal d'audit | Audit log | |
| permission | Recht | Droit | Permission | Permissiecódes zelf zijn technisch en blijven Engels. |
| role | Rol | Rôle | Role | |
| tenant | Bedrijf/omgeving | Société/environnement | Tenant | In UI zelden letterlijk tonen. |

## Vaste beslissingen bij ambiguïteit

- **rit vs. opdracht**: "rit/tournée/trip" = uitvoering met voertuig; "opdracht/commande/
  order" = klantvraag. Nooit door elkaar.
- **locatie**: "Locatie/Site/Location" — FR bewust "site" (bedrijfsterrein), niet
  "emplacement" (dat is een magazijnpositie: warehouse location = emplacement).
- **melding vs. bericht**: notificatie = "melding/notification/notification"; menselijk
  bericht = "bericht/message/message".
- **aanwezigheid**: modulenaam Presences (FR "Présences"); de handeling is "pointage".
