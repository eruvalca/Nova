export const POLICY_VERSION = 1;
export const SUITES = Object.freeze({
  unit: 'Nova.Unit.Tests/Nova.Unit.Tests.csproj',
  integration: 'Nova.Integration.Tests/Nova.Integration.Tests.csproj',
  browser: 'Nova.Browser.Tests/Nova.Browser.Tests.csproj',
});
export const FULL_PROFILES = new Set(['pre-pr', 'pre-merge', 'ci']);
export const PROFILES = new Set(['quick', 'push', ...FULL_PROFILES]);

export function verificationPlan({ profile, paths = [], suites = [], filters = [] }) {
  if (!PROFILES.has(profile)) throw new Error('Unknown profile: ' + profile);
  if (filters.length && profile !== 'quick') throw new Error('Only quick accepts test filters.');
  if (suites.length && profile !== 'quick') throw new Error('Only quick accepts explicit suites.');
  if (suites.some(suite => !SUITES[suite])) throw new Error('Unknown suite.');
  if (profile === 'quick' && !suites.length) throw new Error('quick requires --suite unit, integration, or browser.');
  const selected = new Set(profile === 'quick' ? suites : ['unit']);
  const reasons = [];
  let contrast = FULL_PROFILES.has(profile);
  if (FULL_PROFILES.has(profile)) {
    Object.keys(SUITES).forEach(suite => selected.add(suite));
    reasons.push('Full profiles always execute all suites freshly.');
  } else if (profile === 'push') {
    for (const path of paths) {
      const p = path.replaceAll('\\', '/');
      if (/\.(csproj|props|targets|slnx)$/.test(p) || /(^|\/)(global\.json|NuGet\.config|\.editorconfig|\.node-version)$/.test(p)
        || p === 'Nova/package.json' || p === 'Nova/package-lock.json') {
        selected.add('integration'); selected.add('browser'); reasons.push(p + ': project/package configuration');
        if (p.startsWith('Nova/package')) contrast = true;
      } else if (/^Nova\.(?:UI|Client)\/(?:.*\/)?(?:Auth[^/]*|Security|Identity)(?:\/|\.)/.test(p) || p.startsWith('Nova.UI/Shared/State/')) {
        selected.add('integration'); selected.add('browser'); reasons.push(p + ': authentication/identity boundary');
      } else if (/^(Nova\/scss\/|Nova\/scripts\/)/.test(p)) {
        contrast = true; selected.add('browser'); reasons.push(p + ': theme/asset boundary');
      } else if (p.startsWith('Nova.Client/Services/')) {
        selected.add('integration'); selected.add('browser'); reasons.push(p + ': HTTP client contract boundary');
      } else if (/^(Nova\.UI\/|Nova\.Client\/|Nova\/Components\/|Nova\/wwwroot\/)/.test(p)) {
        selected.add('browser'); reasons.push(p + ': interactive/client boundary');
      } else if (p.startsWith('Nova.Browser.Tests/')) {
        selected.add('browser'); reasons.push(p + ': browser coverage');
      } else if (/^(Nova\/|Nova\.Shared\/|Nova\.Integration\.Tests\/|Nova\.AppHost\/|Nova\.ServiceDefaults\/|eng\/|\.github\/workflows\/)/.test(p)
        || /\.(csproj|props|targets|slnx)$/.test(p) || /(^|\/)(global\.json|NuGet\.config|\.editorconfig|\.node-version)$/.test(p)) {
        selected.add('integration'); selected.add('browser'); reasons.push(p + ': shared/server/build boundary');
      } else if (p.startsWith('Nova.Unit.Tests/') || /\.(md|png|jpg|jpeg|webp|svg)$/.test(p)) {
        reasons.push(p + ': unit plus engineering checks');
      } else {
        selected.add('integration'); selected.add('browser'); reasons.push(p + ': unclassified input; all suites');
      }
    }
  }
  const checks = profile === 'quick' ? ['build'] : ['engineering', 'guidance', 'build', 'format'];
  if (contrast) checks.push('contrast');
  checks.push(...Object.keys(SUITES).filter(suite => selected.has(suite)));
  return { profile, checks, reasons, filters, suites: Object.keys(SUITES).filter(suite => selected.has(suite)) };
}

export const OPTIONAL_SCREENSHOTS = new Set([
  'Nova.Browser.Tests.CampaignCloseoutBrowserTests.Closeout_A11yEvidence_CapturesScreenshots',
  'Nova.Browser.Tests.CampaignEvaluationBrowserTests.A11yManualChecklist_CapturesContrastAndTouchTargetEvidence',
  'Nova.Browser.Tests.LandingPageBrowserTests.Landing_A11yEvidence_CapturesScreenshots',
  'Nova.Browser.Tests.DashboardBrowserTests.Dashboard_A11yEvidence_CapturesScreenshots',
  'Nova.Browser.Tests.CampaignFormBrowserTests.CampaignForm_A11yEvidence_CapturesScreenshots',
  'Nova.Browser.Tests.TeamFormBrowserTests.TeamDetail_A11yEvidence_CapturesScreenshots',
  'Nova.Browser.Tests.PlayerFormBrowserTests.PlayerDetail_A11yEvidence_CapturesScreenshots',
]);

// xUnit v4 emits CTRF JSON natively; parsing it needs no XML/npm dependency.
export function assessReport(report, { suite, screenshots = false } = {}) {
  const result = report?.results;
  if (report?.reportFormat !== 'CTRF' || !Array.isArray(result?.tests) || !result?.summary) throw new Error('Missing or malformed CTRF report.');
  const tests = result.tests;
  const counts = { passed: 0, failed: 0, skipped: 0, pending: 0, other: 0 };
  const unexpected = [];
  for (const test of tests) {
    if (typeof test.name !== 'string' || !Object.hasOwn(counts, test.status)) throw new Error('Malformed test result.');
    counts[test.status]++;
    if (test.status === 'skipped' && !(suite === 'browser' && !screenshots && OPTIONAL_SCREENSHOTS.has(test.name))) unexpected.push(test.name);
  }
  for (const [name, count] of Object.entries(counts)) {
    if (result.summary[name] !== count) throw new Error('Inconsistent report count: ' + name);
  }
  if (result.summary.tests !== tests.length) throw new Error('Inconsistent total test count.');
  const errors = (result.extra?.suites || []).flatMap(value => value.errors || []);
  const executed = counts.passed + counts.failed;
  return {
    total: tests.length, executed, ...counts, unexpectedSkips: unexpected, infrastructureErrors: errors,
    // Deliberately separate execution outcome from test-coverage adequacy.
    verdict: executed > 0 && counts.failed === 0 && counts.pending === 0 && counts.other === 0 && unexpected.length === 0 && errors.length === 0 ? 'passed' : 'failed',
  };
}
