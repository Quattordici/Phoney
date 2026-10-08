// Exports the raw (unmerged) faker.js locale definitions to JSON so the
// Phonery data compiler can turn them into Phonery's binary locale resources.
//
// Usage: node export-locales.mjs <outDir>
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { allLocales, faker } from '@faker-js/faker';

const outDir = path.resolve(process.argv[2] ?? '../../data/raw');
const require = createRequire(import.meta.url);
const fakerVersion = require('@faker-js/faker/package.json').version;

fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(outDir, { recursive: true });

const codes = Object.keys(allLocales).sort();
const codeSet = new Set(codes);

// faker.js builds each locale's fallback chain as: the locale itself, every
// existing prefix (de_AT -> de), then en and base.
function fallbackChain(code) {
  if (code === 'base') return ['base'];
  const chain = [code];
  let current = code;
  while (current.includes('_')) {
    current = current.slice(0, current.lastIndexOf('_'));
    if (codeSet.has(current)) chain.push(current);
  }
  for (const tail of ['en', 'base']) if (!chain.includes(tail)) chain.push(tail);
  return chain;
}

const locales = [];
for (const code of codes) {
  const definition = allLocales[code];
  fs.writeFileSync(path.join(outDir, `${code}.json`), JSON.stringify(definition, null, 1) + '\n');
  locales.push({ code, fallback: fallbackChain(code).slice(1), metadata: definition.metadata ?? {} });
}

// faker.js resolves template expressions such as {{person.firstName}} against its
// module methods before falling back to locale data, so the compiler needs the method list.
const methods = [];
for (const moduleName of Object.keys(faker).sort()) {
  const module = faker[moduleName];
  if (module == null || typeof module !== 'object' || moduleName.startsWith('_') || moduleName === 'definitions' || moduleName === 'rawDefinitions') continue;
  const names = new Set();
  for (let proto = module; proto && proto !== Object.prototype; proto = Object.getPrototypeOf(proto)) {
    for (const name of Object.getOwnPropertyNames(proto)) {
      if (name !== 'constructor' && !name.startsWith('_') && typeof module[name] === 'function') names.add(name);
    }
  }
  for (const name of [...names].sort()) methods.push(`${moduleName}.${name}`);
}

fs.writeFileSync(
  path.join(outDir, '_manifest.json'),
  JSON.stringify({ fakerVersion, locales, methods }, null, 2) + '\n',
);
console.log(`Exported ${codes.length} locales from @faker-js/faker ${fakerVersion} to ${outDir}`);
