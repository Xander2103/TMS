// Draws arrows, numbered callouts and boxes over a real screenshot. The application is never touched:
// the positions come from the live DOM (Playwright bounding boxes) and the drawing happens on a separate
// page that only holds the captured image, an SVG layer and a legend.
const COLOR = '#d9134f'
const CIRCLED = ['①', '②', '③', '④', '⑤', '⑥', '⑦', '⑧', '⑨']

const overlaps = (a, b, pad = 0) =>
  a.x < b.x + b.width + pad && b.x < a.x + a.width + pad && a.y < b.y + b.height + pad && b.y < a.y + a.height + pad

/** Candidate badge centres around a box, most natural first; `side` moves one side to the front. */
function candidates(box, r, side) {
  const cx = box.x + box.width / 2
  const cy = box.y + box.height / 2
  const out = []
  for (const dist of [64, 96, 136, 180]) {
    const byName = {
      right: { x: box.x + box.width + dist, y: cy },
      left: { x: box.x - dist, y: cy },
      bottom: { x: cx, y: box.y + box.height + dist },
      top: { x: cx, y: box.y - dist },
      'bottom-right': { x: box.x + box.width + dist * 0.7, y: box.y + box.height + dist * 0.7 },
      'bottom-left': { x: box.x - dist * 0.7, y: box.y + box.height + dist * 0.7 },
      'top-right': { x: box.x + box.width + dist * 0.7, y: box.y - dist * 0.7 },
      'top-left': { x: box.x - dist * 0.7, y: box.y - dist * 0.7 },
    }
    if (side && byName[side]) out.push(byName[side])
    out.push(...Object.values(byName))
  }
  return out.map((c) => ({ ...c, r }))
}

/** Places a badge for every target so that no badge covers any target or another badge. */
export function placeBadges(targets, width, height, r) {
  const placed = []
  for (const t of targets) {
    const options = candidates(t.box, r, t.side)
    const fits = (c) => {
      const b = { x: c.x - r, y: c.y - r, width: 2 * r, height: 2 * r }
      if (b.x < 2 || b.y < 2 || b.x + b.width > width - 2 || b.y + b.height > height - 2) return false
      if (targets.some((o) => overlaps(b, o.box, 4))) return false
      return !placed.some((p) => overlaps(b, { x: p.x - r, y: p.y - r, width: 2 * r, height: 2 * r }, 8))
    }
    const chosen = options.find(fits)
    if (!chosen) throw new Error(`no free spot for callout next to box ${JSON.stringify(t.box)}`)
    placed.push(chosen)
  }
  return placed
}

/** The point on the edge of `box` closest to (x, y), a few pixels outside. */
function edgePoint(box, x, y, gap = 5) {
  const px = Math.max(box.x - gap, Math.min(x, box.x + box.width + gap))
  const py = Math.max(box.y - gap, Math.min(y, box.y + box.height + gap))
  return { x: px, y: py }
}

const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;')

/** @param targets [{ box, label, side?, noBox? }] in CSS pixels of the screenshot */
export function annotationHtml({ imageDataUrl, width, height, targets, mobile = false }) {
  const r = mobile ? 15 : 20
  const fontBadge = mobile ? 17 : 23
  const legendFont = mobile ? 16 : 25
  const placed = placeBadges(targets.filter((t) => !t.corner), width, height, r)
  let next = 0
  // A corner badge sits on the corner of its box without an arrow, for wide targets on a narrow phone screen.
  const badges = targets.map((t) => (t.corner ? { x: Math.min(t.box.x + t.box.width - r, width - r - 2), y: Math.max(t.box.y, r + 2) } : placed[next++]))
  const parts = []
  targets.forEach((t, i) => {
    const b = badges[i]
    const box = t.box
    if (!t.noBox) {
      parts.push(`<rect x="${box.x - 4}" y="${box.y - 4}" width="${box.width + 8}" height="${box.height + 8}" rx="7" fill="none" stroke="#fff" stroke-width="6"/>`)
      parts.push(`<rect x="${box.x - 4}" y="${box.y - 4}" width="${box.width + 8}" height="${box.height + 8}" rx="7" fill="none" stroke="${COLOR}" stroke-width="3"/>`)
    }
    if (!t.corner) {
      const end = edgePoint(box, b.x, b.y, t.noBox ? 2 : 7)
      const dx = end.x - b.x
      const dy = end.y - b.y
      const len = Math.hypot(dx, dy) || 1
      const sx = b.x + (dx / len) * r
      const sy = b.y + (dy / len) * r
      parts.push(`<line x1="${sx}" y1="${sy}" x2="${end.x}" y2="${end.y}" stroke="#fff" stroke-width="7" stroke-linecap="round"/>`)
      parts.push(`<line x1="${sx}" y1="${sy}" x2="${end.x}" y2="${end.y}" stroke="${COLOR}" stroke-width="4" marker-end="url(#head)"/>`)
    }
    parts.push(`<circle cx="${b.x}" cy="${b.y}" r="${r}" fill="${COLOR}" stroke="#fff" stroke-width="3"/>`)
    parts.push(`<text x="${b.x}" y="${b.y + fontBadge * 0.36}" text-anchor="middle" font-size="${fontBadge}" font-weight="700" fill="#fff">${i + 1}</text>`)
  })
  const legend = targets
    .map((t, i) => `<div class="row"><span class="n">${CIRCLED[i] ?? i + 1}</span><span>${esc(t.label)}</span></div>`)
    .join('')
  return `<!doctype html><meta charset="utf-8"><style>
    html,body{margin:0;background:#fff;font-family:-apple-system,"Helvetica Neue",Arial,sans-serif}
    #canvas{width:${width}px;position:relative}
    #shot{position:relative;width:${width}px;height:${height}px}
    #shot img{display:block;width:${width}px;height:${height}px}
    #shot svg{position:absolute;left:0;top:0}
    #legend{padding:${mobile ? 10 : 14}px ${mobile ? 10 : 20}px;background:#fff;border-top:3px solid ${COLOR};font-size:${legendFont}px;line-height:1.3;color:#111;font-weight:600}
    .row{display:flex;gap:.45em;margin:${mobile ? 4 : 5}px 0}
    .n{color:${COLOR};font-weight:700;flex:none}
  </style><div id="canvas"><div id="shot"><img src="${imageDataUrl}" alt="">
  <svg width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" font-family="-apple-system,Helvetica,Arial,sans-serif">
    <defs><marker id="head" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="${mobile ? 3.6 : 4}" markerHeight="${mobile ? 3.6 : 4}" orient="auto-start-reverse"><path d="M0 0L10 5L0 10z" fill="${COLOR}"/></marker></defs>
    ${parts.join('\n')}
  </svg></div><div id="legend">${legend}</div></div>`
}
