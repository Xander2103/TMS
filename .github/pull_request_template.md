## Wat verandert er

<!-- korte beschrijving -->

## Security-checklist

- [ ] Nieuwe endpoints hebben `[RequirePermission]`/`[Authorize]` (fallback-policy dekt de rest, maar wees expliciet).
- [ ] Nieuwe tenant-entiteiten implementeren `ITenantOwned` (globale filter + architectuurtest).
- [ ] Nieuwe queries buiten request-scope (jobs/seeders) zijn bewust tenant-agnostisch en filteren zelf.
- [ ] Uploads gaan door extensie-whitelist + `UploadValidation` (magic bytes); geen SVG.
- [ ] Geen secrets/tokens/wachtwoorden in code, config, logs of auditpayloads.
- [ ] Gevoelige reads (medisch/vertrouwelijk/bulk-export) hebben een read-audit.
- [ ] Migraties zijn additief of hebben een gedocumenteerd datamigratiepad.
- [ ] Volledige backend- en frontendsuite groen.
