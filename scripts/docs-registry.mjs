// Shared reader for the documentation registries under docs/.
//
// The registries describe a small graph of artifacts: backlog entries record
// known gaps, RFCs record open architectural questions, and both resolve into
// OpenSpec changes. Nothing here writes; the callers decide what to report.
//
// The YAML reader below accepts only the restricted shape the registries use:
// nested mappings, sequences of scalars, and folded block scalars. Anything
// else throws with a file and line, so an unsupported construct fails loudly
// instead of being misread. That strictness is deliberate — it keeps the
// registry format small enough to stay hand-editable, which is the same reason
// the model sidecars are plain text.

import { readdir, readFile, stat } from 'node:fs/promises';
import { join, relative, resolve, sep } from 'node:path';

export const repositoryRoot = resolve(import.meta.dirname, '..');
export const documentationRoot = join(repositoryRoot, 'docs');
export const changesRoot = join(repositoryRoot, 'openspec', 'changes');

/** Reads the restricted YAML subset the registries are written in. */
export function parseRegistry(source, file) {
  const lines = source.split(/\r?\n/);
  let cursor = 0;

  const fail = (message, at = cursor) => {
    throw new Error(`${file}:${at + 1}: ${message}`);
  };
  const blank = (line) => line.trim() === '' || line.trimStart().startsWith('#');
  const indentOf = (line) => line.length - line.trimStart().length;
  const skipBlank = () => {
    while (cursor < lines.length && blank(lines[cursor])) cursor += 1;
  };

  const scalar = (token, at) => {
    const value = token.trim();
    if (value === '' || value === 'null' || value === '~') return null;
    if (value === '[]') return [];
    if (value === '{}') return {};
    if (value === 'true') return true;
    if (value === 'false') return false;
    if (/^-?\d+$/.test(value)) return Number(value);
    const quoted =
      (value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"));
    if (quoted) return value.slice(1, -1);
    // Relations are written as one-line flow mappings so a relation stays a
    // single readable line: { type: produces, target: some-change }.
    if (value.startsWith('{') && value.endsWith('}')) {
      const node = {};
      for (const pair of value.slice(1, -1).split(',')) {
        if (pair.trim() === '') continue;
        const colon = pair.indexOf(':');
        if (colon === -1) fail(`flow mapping entry ${pair.trim()} is not "key: value"`, at);
        node[pair.slice(0, colon).trim()] = scalar(pair.slice(colon + 1), at);
      }
      return node;
    }
    if (value.includes(': ')) fail('scalar contains ": " and must be quoted', at);
    return value;
  };

  // Folded and literal blocks are joined into one line: the registries use them
  // only for prose, and no consumer depends on the original line breaks.
  const readBlock = (indent) => {
    const parts = [];
    while (cursor < lines.length) {
      const line = lines[cursor];
      if (line.trim() !== '' && indentOf(line) <= indent) break;
      parts.push(line.trim());
      cursor += 1;
    }
    return parts.join(' ').trim();
  };

  const parseSequence = (indent) => {
    const items = [];
    while (cursor < lines.length) {
      skipBlank();
      if (cursor >= lines.length) break;
      const line = lines[cursor];
      if (indentOf(line) !== indent) break;
      const body = line.trimStart();
      if (!body.startsWith('- ')) break;
      items.push(scalar(body.slice(2), cursor));
      cursor += 1;
    }
    return items;
  };

  const parseMapping = (indent) => {
    const node = {};
    while (cursor < lines.length) {
      skipBlank();
      if (cursor >= lines.length) break;
      const line = lines[cursor];
      const actual = indentOf(line);
      if (actual < indent) break;
      if (actual > indent) fail(`unexpected indentation ${actual}, expected ${indent}`);
      const body = line.trimStart();
      if (body.startsWith('- ')) break;
      const colon = body.indexOf(':');
      if (colon === -1) fail('expected "key: value"');
      const key = body.slice(0, colon).trim();
      const rest = body.slice(colon + 1).trim();
      const at = cursor;
      cursor += 1;
      if (['>', '>-', '|', '|-'].includes(rest)) node[key] = readBlock(indent);
      else if (rest === '') node[key] = parseNode(indent + 1);
      else node[key] = scalar(rest, at);
    }
    return node;
  };

  const parseNode = (indent) => {
    skipBlank();
    if (cursor >= lines.length) return null;
    const actual = indentOf(lines[cursor]);
    if (actual < indent) return null;
    return lines[cursor].trimStart().startsWith('- ')
      ? parseSequence(actual)
      : parseMapping(actual);
  };

  return parseNode(0) ?? {};
}

const asList = (value) => (Array.isArray(value) ? value : value == null ? [] : [value]);

/**
 * File extensions a kind directory may hold. A diagram is an artifact like any
 * other: registering it means a rename or a deletion fails validation instead
 * of leaving a document pointing at nothing. PlantUML earns its place here by
 * being text — it diffs, it reviews, and it needs no rendering step to read.
 */
const INDEXED_EXTENSIONS = ['.md', '.puml'];

/** Every indexed document beneath directory, as paths relative to it, posix-style. */
async function documentsBeneath(directory) {
  const found = [];
  const walk = async (current) => {
    let entries;
    try {
      entries = await readdir(current, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      const path = join(current, entry.name);
      if (entry.isDirectory()) await walk(path);
      else if (INDEXED_EXTENSIONS.some((extension) => entry.name.endsWith(extension)))
        found.push(relative(directory, path).split(sep).join('/'));
    }
  };
  await walk(directory);
  return found.sort();
}

/** Change identifiers OpenSpec knows about, active and archived. */
export async function loadChanges() {
  const read = async (directory) => {
    try {
      const entries = await readdir(directory, { withFileTypes: true });
      return entries.filter((entry) => entry.isDirectory()).map((entry) => entry.name);
    } catch {
      return [];
    }
  };
  const active = (await read(changesRoot)).filter((name) => name !== 'archive');
  // Archived changes are stored as <YYYY-MM-DD>-<name>; compare on the name.
  const archived = (await read(join(changesRoot, 'archive'))).map((name) =>
    name.replace(/^\d{4}-\d{2}-\d{2}-/, ''),
  );
  return { active: new Set(active), archived: new Set(archived) };
}

/**
 * The artifact kinds: the directory each lives in, the states it may hold, and
 * whether it ever closes. Only kinds that close are archived by moving the
 * document; the rest stay in place and change state, because a fact that was
 * refuted has to remain findable to stop it being rediscovered.
 */
export const KINDS = {
  story: { dir: 'stories', states: ['current', 'superseded'], closes: false },
  finding: { dir: 'findings', states: ['current', 'superseded', 'refuted'], closes: false },
  usecase: { dir: 'usecases', states: ['current', 'superseded'], closes: false },
  // A decision records what was settled and is binding until something replaces
  // it. It does not close: a closed artifact is finished work, while a decision
  // stays in force. It is superseded, never archived, for the same reason a
  // refuted finding stays put — the record exists to stop the question being
  // reopened from scratch.
  decision: { dir: 'decisions', states: ['current', 'superseded'], closes: false },
  backlog: { dir: 'backlog', states: ['open', 'blocked', 'closed', 'superseded'], closes: true },
  rfc: { dir: 'rfcs', states: ['open', 'closed', 'superseded'], closes: true },
};

export const RELATIONS = ['motivated-by', 'produces', 'blocked-by', 'supersedes', 'relates-to'];

// An OpenSpec change and the artifact that asked for it routinely share a name.
// Prefixing the change makes the target unambiguous instead of silently
// resolving to the artifact itself.
export const CHANGE_PREFIX = 'change:';
export const isChangeTarget = (target) =>
  typeof target === 'string' && target.startsWith(CHANGE_PREFIX);
export const changeName = (target) => target.slice(CHANGE_PREFIX.length);

/** Relations declared by an artifact, normalised and validated in shape. */
export function relationsOf(entry) {
  return asList(entry?.relations)
    .filter((relation) => relation && typeof relation === 'object')
    .map((relation) => ({ type: relation.type, target: relation.target }));
}

/** Relations pointing at an identifier, as { source, type } pairs. */
export function inboundOf(artifacts, id) {
  const inbound = [];
  for (const [source, entry] of Object.entries(artifacts))
    for (const relation of relationsOf(entry))
      if (relation.target === id) inbound.push({ source, type: relation.type });
  return inbound;
}

/**
 * Loads the single documentation registry: artifacts keyed by identifier, plus
 * every indexed document present beneath docs/ so unregistered ones can be
 * found.
 */
export async function loadArtifacts() {
  const file = join(documentationRoot, 'registry.yaml');
  await stat(file);
  const where = relative(repositoryRoot, file).split(sep).join('/');
  const document = parseRegistry(await readFile(file, 'utf8'), where);
  const artifacts = document?.artifacts;
  if (artifacts == null || typeof artifacts !== 'object' || Array.isArray(artifacts))
    throw new Error(`${where}: expected a top-level "artifacts:" mapping`);
  // Only the kind directories hold artifacts. Loose reference material
  // elsewhere under docs/ is not indexed and is not an orphan.
  const files = [];
  for (const { dir } of Object.values(KINDS))
    for (const found of await documentsBeneath(join(documentationRoot, dir)))
      files.push(`${dir}/${found}`);
  return { file, where, artifacts, files: files.sort() };
}

export { asList, documentsBeneath, INDEXED_EXTENSIONS };
