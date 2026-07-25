import { readdir, rm } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';

const repositoryRoot = resolve(import.meta.dirname, '..');
const home = resolve(repositoryRoot, 'home');
const developmentTemporary = resolve(repositoryRoot, '.tmp', 'dev');
if (dirname(home) !== repositoryRoot) throw new Error(`Refusing to clean unexpected path: ${home}`);
if (dirname(dirname(developmentTemporary)) !== repositoryRoot)
  throw new Error(`Refusing to clean unexpected path: ${developmentTemporary}`);

for (const entry of await readdir(home, { withFileTypes: true })) {
  if (entry.name !== '.gitkeep') await rm(resolve(home, entry.name), { force: true, recursive: true });
}

await rm(developmentTemporary, { force: true, recursive: true });
