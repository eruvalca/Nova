// Run from the repository root: node .impeccable/review/issue-198/measure-sheet.mjs
// Analysis of immutable captures only; no production UI or shared scoring changes.
import fs from 'node:fs';
import crypto from 'node:crypto';
import { readPng, compare, writeArtifacts, buildReport, resolveRegions, scorePair, verdictFor, inkBox } from '../../../.agents/skills/impeccable/scripts/comp-diff.mjs';
import { crop } from '../../../.agents/skills/impeccable/scripts/lib/raster.mjs';
import { encodePng } from '../../../.agents/skills/impeccable/scripts/lib/png.mjs';

const root = '.impeccable/review/issue-198';
const out = `${root}/sheet-relative`;
fs.mkdirSync(out, { recursive: true });
const compPath = '.impeccable/mocks/decision/issue-198-shared-notebook.png';
const buildPath = `${root}/captures/hero.png`;
const comp = readPng(compPath), build = readPng(buildPath);
const original = JSON.parse(fs.readFileSync('.impeccable/build/spec.json'));
// Locate the long horizontal border near the visually established origin.
// This depends on border pixels, never on a similarity-score search.
const borderY = (img, start, end) => {
  const rows = [];
  for (let y = start; y <= end; y++) {
    let total = 0;
    for (let x = 100; x <= 800; x++) {
      const i = (y * img.width + x) * 4;
      total += img.data[i] + img.data[i + 1] + img.data[i + 2];
    }
    rows.push({ y, mean: total / (701 * 3) });
  }
  return rows.reduce((a, b) => a.mean < b.mean ? a : b).y;
};
const compY = borderY(comp, 473, 478), buildY = borderY(build, 586, 591);
const height = 1667 - buildY;
const bounds = { comp: { x: 59, y: compY, w: 780, h: height }, build: { x: 59, y: buildY, w: 780, h: height } };
const shellIds = ['campaign-back', 'campaign-heading', 'season', 'participant-count', 'menu', 'route', 'readiness', 'bottom-nav'];
const included = original.regions.filter(r => !shellIds.includes(r.id) && r.id !== 'older');
const spec = { ...original, comp: `${out}/comp.png`, compSize: { width: 780, height }, regions: included.map(r => ({ ...r, box: {
  x: (r.box.x * comp.width - bounds.comp.x) / 780,
  y: (r.box.y * comp.height - bounds.comp.y) / height,
  w: r.box.w * comp.width / 780,
  h: r.box.h * comp.height / height
} })) };
for (const r of spec.regions) {
  if (r.box.x < 0 || r.box.y < 0 || r.box.x + r.box.w > 1 || r.box.y + r.box.h > 1) throw new Error(`Outside crop: ${r.id}`);
}
const a = crop(comp, ...Object.values(bounds.comp));
const b = crop(build, ...Object.values(bounds.build));
fs.writeFileSync(`${out}/comp.png`, encodePng(a));
fs.writeFileSync(`${out}/build.png`, encodePng(b));
fs.writeFileSync(`${out}/spec.json`, JSON.stringify(spec, null, 2) + '\n');
const result = compare({ comp: a, build: b, spec, label: 'Evaluate sheet — approved border registration' });
const directWhole = scorePair(a, b, null);
if (directWhole.overall !== result.whole.overall) throw new Error('Whole score must equal the unshifted native crops.');
const automaticShift = result.shift;
const standardFiles = writeArtifacts(result, a, `${out}/standard-tool`);
fs.writeFileSync(`${out}/standard-tool/report.json`, JSON.stringify(buildReport(result, standardFiles, { additionalRegionalShift: automaticShift }), null, 2) + '\n');
// Border-only registration: identical shared score/verdict functions and identical
// regionCrop minimum-size rule, without comp-diff's additional bestShift search.
const regionCrop = (img, r) => {
  let x = r.x * img.width, y = r.y * img.height, w = r.w * img.width, h = r.h * img.height;
  if (h < 48) { y -= (48 - h) / 2; h = 48; }
  if (w < 48) { x -= (48 - w) / 2; w = 48; }
  return crop(img, x, y, w, h);
};
result.regions = resolveRegions(a, spec).map(r => {
  const ca = regionCrop(a, r), cb = regionCrop(b, r);
  const { _detail, _bands, ...score } = scorePair(ca, cb, r.kind);
  return { ...r, score, verdict: verdictFor(score, r.kind), inkBox: { comp: inkBox(ca), build: inkBox(cb) }, _a: ca, _b: cb };
});
result.shift = { dx: 0, dy: 0 };
result.alignedShifted = b;
const files = writeArtifacts(result, a, out);
const report = buildReport(result, files, { comp: `${out}/comp.png`, build: `${out}/build.png`, spec: `${out}/spec.json` });
fs.writeFileSync(`${out}/report.json`, JSON.stringify(report, null, 2) + '\n');
const sha256 = path => crypto.createHash('sha256').update(fs.readFileSync(path)).digest('hex');
// Retain native scanline samples around the independently identified border.
const scanline = (img, y) => {
  const values = [];
  for (const x of [58, 59, 60, 61, 100, 400, 800, 837, 838, 839]) {
    const i = (y * img.width + x) * 4;
    values.push({ x, rgb: [...img.data.slice(i, i + 3)] });
  }
  return { y, values };
};
const manifest = {
  sourceRevision: '381d501950d064d426ddce7c772fe449c485cfde',
  approval: { question: 'May I measure Evaluate relative to its sheet, reviewing the shell separately? Implementation is committed; PR delivery remains pending this decision.', answer: 'Yes please feel free to do so', scope: 'Sheet-relative comparison and separate preserved-shell review. No overall threshold reduction.' },
  sources: [{ path: compPath, sha256: sha256(compPath) }, { path: buildPath, sha256: sha256(buildPath) }],
  viewport: { cssWidth: 598, cssHeight: 1168, devicePixelRatio: 1.5, scrollY: 0 },
  nativePixelBounds: bounds, registration: { dx: 0, dy: buildY - compY, cssDy: (buildY - compY) / 1.5, resizing: false, perElementTransforms: false, borderDetection: 'Minimum mean RGB across x=100..800 in the preidentified six-row border neighborhood.' },
  scoring: { overallThreshold: 0.72, directUnshiftedWholeScore: directWhole.overall, sharedToolUnchanged: true, additionalRegionalShift: result.shift, standardToolRegionalShift: automaticShift, standardToolReport: 'standard-tool/report.json', method: 'Same shared scorePair/verdictFor and minimum48px regionCrop; only automatic bestShift omitted for the one-translation registered report.' },
  excludedShellRegions: shellIds,
  measuredRegions: included.map(r => r.id),
  outsideUnobscuredHeroCoverage: [{ id: 'older', reason: 'The translated original region intersects bottom navigation beginning at physical y=1667. Neither missing nor matched by this measurement. Reviewed separately in original mobile/desktop full-page captures; no same-scale quantitative score claimed.' }],
  borderEvidence: { comp: [474, 475, 476, 477].map(y => scanline(comp, y)), build: [588, 589, 590, 591].map(y => scanline(build, y)) }
};
fs.writeFileSync(`${out}/manifest.json`, JSON.stringify(manifest, null, 2) + '\n');
console.log(JSON.stringify({ overall: report.overall, shift: result.shift, regions: report.regions.map(r => ({ id: r.id, verdict: r.verdict, score: r.score.overall })) }, null, 2));
