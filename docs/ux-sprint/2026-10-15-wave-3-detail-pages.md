# Wave 3A completion + Wave 3B detail pages (2026-10-15)

## Wave 3A discovery

Wave 3A (B14, B25, B26, B31, B44, B47, B49, B50, B52, B53, B54, B55) was already on the branch
(commits 971bf7a7..be66b335) and verified in code, tests and the browser. One gap was found in
B14: on Afwijkingen the "Gemeld" timestamp and on Afwezigheden the sortable "Van" date were cut
with an ellipsis, the exception type wrapped, and the description clipped without an ellipsis.
Fixed in 4e7cad29; `backup/ux-wave-3a-complete` points there.

## Shared entity-detail model (f92300c0, 3f5afd08)

- `PageHeader` `badges`: status badges on the title line.
- `DetailCard` / `DetailGrid`: one flat block per logical group with one quiet card action.
- `FactList`: compact label/value facts, one or two columns.
- `AttentionList`: only what needs action, worst severity colours the strip, each item jumps to
  the block that resolves it. Renders nothing when empty.
- `useDetailTab`: the single section-navigation layer in `?tab=`, with redirects for retired ids.
- `Tabs`: one line, scrolls sideways instead of wrapping a lone tab onto a second row.
- Header order everywhere (as on the dossier): secondary actions · Meer ▾ · primary/next action.
  Destructive actions are last in Meer, in red, behind a confirmation.

## Feature maps (old → new)

### B15 Employee
| Old | New |
|---|---|
| Overzicht = whole editable EmployeeForm + 10-item rail | Overzicht read-only cards, Bewerken per card (header Bewerken opens all) |
| Rail: Kwalificaties, Documenten, Verlofsaldo, Bedrijfsmiddelen | Removed duplicates; the tabs remain |
| Rail: Chauffeursgegevens; tab Ritten | Tab Chauffeur |
| Tabs Verlof & afwezigheden, Urenregistratie | Tab Verlof & tijd |
| Notities (rail / read-only card) | Notities card on Overzicht |
| "Chauffeursprofiel aanmaken" link | Meer ▾ |
| Redirects | `?tab=verlofsaldo|afwezigheden|uren → verlof`, `ritten|chauffeursprofiel → chauffeur`; `?section=` → owning tab/card |

### B16 Vehicle
| Old tab | New |
|---|---|
| Overzicht | Overzicht: attention + Status & inzet, Toewijzing, Techniek & uitrusting, Gepland |
| Techniek, Toewijzing | Techniek & toewijzing |
| Documenten, Keuringen, Tachograaf | Documenten & keuringen |
| Onderhoud, Schade | Onderhoud & schade |
| Leasing, Brandstof, KPI | Leasing & kosten |
| Historiek | Historiek |
| Redirects | old ids → group + `&focus=<block>` |

### B17 Customer
| Old | New |
|---|---|
| Klantgegevens tab | Overzicht: Klantgegevens + Facturatie & fiscaal cards, Open dossiers, Recente facturen |
| "Ter controle" fiscal warnings | Attention strip |
| Nummer wijzigen button | Meer ▾ |
| Documenten per dag (on overview) | Own tab "Documenten per dag" |
| Redirects | `general|locations|contacts|communication|billing|history|messages` → new ids |

**Documenten per dag decision:** it is an operational batch action (preview/download one delivery
day's delivery notes and CMRs for this customer), not customer identity. It is kept in full but
moved off the overview into its own tab, gated by the orders permission as before.

### B24 Incident / Exception
| Old | New |
|---|---|
| Incident: always-editable form | Read-only cards, Bewerken per card, UnsavedChangesGuard kept |
| Incident: Koppelingen collapsed | Koppelingen card open, every link clickable |
| Incident: every transition a button, unconfirmed cancel | One next action; Heropenen/annuleren in Meer (confirmed) |
| Incident: no history | Shared history timeline |
| Exception: one-column dl | Two-column facts card (+ dossier and customer from the DTO) |
| Exception: four transition buttons + assign row | Aan mij toewijzen · Meer ▾ · next action |

The two domains stay separate; only the detail experiences are aligned.

## Known follow-ups (not in scope)
- Trailer detail header still uses the old order (Bewerken · Meer).
- Maintenance/inspection/damage/fuel/tachograph rows keep 2–3 text actions instead of a row menu.
- Invoice list has no customer filter in the URL; "Alle facturen van klant" uses a name search.
- The employee attention strip keeps its own (richer) layout and title "Aandachtspunten".
