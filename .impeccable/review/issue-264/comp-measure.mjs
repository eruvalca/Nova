import { loadRaster } from '../../../.agents/skills/impeccable/scripts/lib/png.mjs';

const REF = '.impeccable/review/issue-264/captures/intake-board-desktop.png';

function px(img, x, y) {
  const i = (y * img.width + x) * 4;
  const d = img.data;
  return 0.2126 * d[i] + 0.7152 * d[i + 1] + 0.0722 * d[i + 2];
}

function sample(img, u, v) {
  const x = Math.min(img.width - 1, Math.floor(u * img.width));
  const y = Math.min(img.height - 1, Math.floor(v * img.height));
  return px(img, x, y);
}

const ref = loadRaster(REF).image;
console.log('reference', ref.width, 'x', ref.height);

for (const id of ['a', 'b', 'c']) {
  const img = loadRaster(`.impeccable/mocks/issue-264-${id}.png`).image;
  let shellSum = 0, shellN = 0, fieldSum = 0, fieldN = 0;
  const N = 240;
  for (let j = 0; j < N; j++) {
    for (let i = 0; i < N; i++) {
      const u = i / N, v = j / N;
      const d = Math.abs(sample(ref, u, v) - sample(img, u, v)) / 255;
      if (u < 0.16) { shellSum += d; shellN++; }
      else if (u > 0.22) { fieldSum += d; fieldN++; }
    }
  }

  // Runs of ink separated by a wide quiet gap within the main field indicate side-by-side boards.
  const bg = sample(img, 0.5, 0.97);
  let twoBoardRows = 0, inkRows = 0, scanned = 0;
  for (let y = 0; y < img.height; y += 2) {
    let clusters = 0, inCluster = false, gap = 0;
    for (let x = Math.floor(img.width * 0.22); x < img.width; x++) {
      const ink = Math.abs(px(img, x, y) - bg) > 14;
      if (ink) {
        if (!inCluster) { clusters++; inCluster = true; }
        gap = 0;
      } else if (inCluster) {
        if (++gap > 40) inCluster = false;
      }
    }
    scanned++;
    if (clusters > 0) inkRows++;
    if (clusters >= 2) twoBoardRows++;
  }

  console.log(
    id,
    'shellDiff', (shellSum / shellN).toFixed(4),
    'fieldDiff', (fieldSum / fieldN).toFixed(4),
    'inkRowPct', (100 * inkRows / scanned).toFixed(1),
    'twoBoardRowPct', (100 * twoBoardRows / scanned).toFixed(1));
}
