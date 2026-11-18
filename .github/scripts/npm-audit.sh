#!/usr/bin/env bash
# The frontend's dependency audit (app.yml, and locally): fails on any high or critical advisory against the runtime
# dependencies (npm audit --omit=dev), except those accepted in audit-exceptions.json. Each exception names one
# advisory and one package, says why it doesn't apply here, and expires within 92 days. An expired exception fails
# too, so it is looked at again; one that no longer matches anything is reported, so it can be removed.
#
# npm audit has no way to accept a single advisory: its only choices are the severity threshold, or `npm audit fix
# --force`, which may downgrade a package by a major version to drop a dependency with no fixed release.
#
# Run from app/frontend:  bash ../../.github/scripts/npm-audit.sh
set -euo pipefail

report="$(mktemp)"
trap 'rm -f "$report"' EXIT

# npm audit exits non-zero whenever it finds anything; the decision is made below.
npm audit --omit=dev --json > "$report" || true

node - "$report" audit-exceptions.json << 'EOF'
const fs = require("node:fs");

const [reportPath, exceptionsPath] = process.argv.slice(2);
const blocking = new Set(["high", "critical"]);
const maxDays = 92;
const today = new Date().toISOString().slice(0, 10);
const daysFromToday = (date) => (Date.parse(date) - Date.parse(today)) / 86_400_000;

const report = JSON.parse(fs.readFileSync(reportPath, "utf8"));
if (report.error) {
  console.log(`::error::npm audit failed: ${report.error.summary ?? JSON.stringify(report.error)}`);
  process.exit(1);
}

const exceptions = fs.existsSync(exceptionsPath) ? JSON.parse(fs.readFileSync(exceptionsPath, "utf8")).exceptions : [];
let failed = false;

for (const exception of exceptions) {
  const valid =
    /^GHSA(-[23456789cfghjmpqrvwx]{4}){3}$/.test(exception.advisory ?? "") &&
    typeof exception.package === "string" && exception.package !== "" &&
    typeof exception.reason === "string" && exception.reason.trim().length >= 40 &&
    /^\d{4}-\d{2}-\d{2}$/.test(exception.expires ?? "") && !Number.isNaN(Date.parse(exception.expires));
  if (!valid) {
    console.log(`::error::${exceptionsPath}: each exception needs "advisory" (a GHSA ID), "package", "reason" (why it doesn't apply here) and "expires" (YYYY-MM-DD): ${JSON.stringify(exception)}`);
    failed = true;
  } else if (daysFromToday(exception.expires) > maxDays) {
    console.log(`::error::${exceptionsPath}: the exception for ${exception.advisory} expires ${exception.expires}, more than ${maxDays} days away. Choose an earlier date and look again then.`);
    failed = true;
  }
}

// The advisories: the objects in each vulnerable package's "via" (its strings name other vulnerable packages, which
// lead back to these).
const advisories = new Map();
for (const vulnerability of Object.values(report.vulnerabilities ?? {})) {
  for (const via of vulnerability.via) {
    if (typeof via === "object") {
      advisories.set(`${via.url.split("/").pop()} ${via.name}`, via);
    }
  }
}

for (const [key, advisory] of advisories) {
  if (!blocking.has(advisory.severity)) continue;
  const [id] = key.split(" ");
  const described = `${advisory.severity}: ${advisory.name} ${advisory.range}, ${advisory.title} (${advisory.url})`;
  const exception = exceptions.find((candidate) => candidate.advisory === id && candidate.package === advisory.name);
  if (!exception) {
    console.log(`::error::${described}. Update the package, or, if it can't affect this app, accept it in app/frontend/${exceptionsPath} (docs/CI_CD.md).`);
    failed = true;
  } else if (exception.expires < today) {
    console.log(`::error::${described}. Its exception expired on ${exception.expires}: update the package if a fixed version exists, else check the reason still holds and choose a new date.`);
    failed = true;
  } else {
    console.log(`::warning::${described}. Accepted until ${exception.expires}: ${exception.reason}`);
  }
}

for (const exception of exceptions) {
  if (!advisories.has(`${exception.advisory} ${exception.package}`)) {
    console.log(`::notice::${exception.advisory} (${exception.package}) no longer affects the runtime dependencies: remove its exception from app/frontend/${exceptionsPath}.`);
  }
}

const counts = report.metadata?.vulnerabilities ?? {};
console.log(`npm audit (runtime dependencies): ${counts.critical ?? 0} critical, ${counts.high ?? 0} high, ${counts.moderate ?? 0} moderate, ${counts.low ?? 0} low.`);
if (failed) process.exit(1);
console.log("No high or critical advisory that isn't accepted.");
EOF
