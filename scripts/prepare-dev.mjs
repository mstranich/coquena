import { mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';

const repositoryRoot = resolve(import.meta.dirname, '..');
const developmentTemporary = resolve(repositoryRoot, '.tmp', 'dev');
if (dirname(dirname(developmentTemporary)) !== repositoryRoot)
  throw new Error(`Refusing to prepare unexpected path: ${developmentTemporary}`);

await mkdir(developmentTemporary, { recursive: true });
