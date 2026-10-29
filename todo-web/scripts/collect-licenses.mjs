import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = fileURLToPath(new URL('..', import.meta.url));
const outputRoot = path.resolve(webRoot, '../licenses/npm');
const lock = JSON.parse(fs.readFileSync(path.join(webRoot, 'package-lock.json'), 'utf8'));
const records = [];
const missing = [];
fs.mkdirSync(outputRoot, { recursive: true });

for (const [lockPath, record] of Object.entries(lock.packages)) {
  if (!lockPath) continue;
  const packageRoot = path.join(webRoot, lockPath);
  const manifestPath = path.join(packageRoot, 'package.json');
  if (!fs.existsSync(manifestPath)) continue; // Optional packages for other platforms.
  const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  const packageKey = `${manifest.name.replaceAll('/', '__')}@${manifest.version}`;
  const notices = fs.readdirSync(packageRoot).filter((name) => /^(licen[sc]e|notice|copying)([._-].*)?$/i.test(name) && fs.statSync(path.join(packageRoot, name)).isFile());
  if (!notices.length) missing.push({ name: manifest.name, version: manifest.version, license: record.license ?? manifest.license ?? 'See package metadata' });
  if (notices.length) {
    const destination = path.join(outputRoot, packageKey);
    fs.mkdirSync(destination, { recursive: true });
    for (const notice of notices) fs.copyFileSync(path.join(packageRoot, notice), path.join(destination, notice));
  }
  records.push({ name: manifest.name, version: manifest.version, license: record.license ?? manifest.license ?? 'See package metadata', notices: notices.map((name) => `${packageKey}/${name}`) });
}

records.sort((a, b) => a.name.localeCompare(b.name) || a.version.localeCompare(b.version));
const rows = records.map((r) => `| ${r.name} | ${r.version} | ${typeof r.license === 'string' ? r.license : JSON.stringify(r.license)} | ${r.notices.map((name) => `[${path.basename(name)}](./${name})`).join(', ') || 'See package metadata'} |`);
const index = ['# npm dependency license index', '', 'Notices for dependencies installed for this build, including build tools. Optional packages for other operating systems remain recorded in package-lock.json.', '', '| Package | Version | Declared license | Notices |', '| --- | --- | --- | --- |', ...rows, ''].join('\n');
fs.writeFileSync(path.join(outputRoot, 'INDEX.md'), index);
fs.writeFileSync(path.join(outputRoot, 'manifest.json'), JSON.stringify(records, null, 2) + '\n');
console.log(`Collected license metadata for ${records.length} installed packages.`);
if (missing.length) console.log(`Packages without a top-level license file: ${JSON.stringify(missing)}`);
