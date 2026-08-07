// Prints the current state of the documentation artifacts in one screen.
//
// This exists for context economy. Whoever picks the work back up should not
// have to open a dozen documents to learn what is open, what is blocked, and on
// what. The registry alone answers that, so this never opens an artifact.
//
// Run with `task docs:status`.

import { KINDS, changeName, inboundOf, isChangeTarget, loadArtifacts, loadChanges, relationsOf } from './docs-registry.mjs';

const { artifacts } = await loadArtifacts();
const changes = await loadChanges();
const changeExists = (id) => changes.active.has(id) || changes.archived.has(id);

const entries = Object.entries(artifacts).filter(([, entry]) => entry && typeof entry === 'object');
const of = (kind) => entries.filter(([, entry]) => entry.kind === kind);
const targets = (entry, type) => relationsOf(entry).filter((r) => r.type === type).map((r) => r.target);

const pad = (value, width) => String(value).padEnd(width);
const table = (rows) => {
  if (rows.length === 0) return '  (none)';
  const widths = rows[0].map((_, i) => rows.reduce((max, row) => Math.max(max, String(row[i]).length), 0));
  return rows
    .map((row) => '  ' + row.map((cell, i) => (i === row.length - 1 ? String(cell) : pad(cell, widths[i]))).join('  ').trimEnd())
    .join('\n');
};

// Durable kinds first: they are the context, not the worklist.
for (const kind of ['story', 'usecase', 'finding']) {
  const rows = of(kind).filter(([, entry]) => entry.state !== 'superseded');
  if (rows.length === 0) continue;
  const { dir } = KINDS[kind];
  console.log(`\n${dir[0].toUpperCase()}${dir.slice(1)}`);
  console.log(
    table(rows.map(([id, entry]) => [entry.state, id, `${inboundOf(artifacts, id).length} ref`, entry.title ?? ''])),
  );
}

const openRFCs = of('rfc').filter(([, entry]) => entry.state === 'open');
console.log('\nRFCs open');
console.log(table(openRFCs.map(([id, entry]) => [id, `${inboundOf(artifacts, id).length} ref`, entry.title ?? ''])));

// Highest priority first, then ready before blocked, so the top of the list is
// what could actually be started now.
const rank = (entry) => (entry.priority === 'high' ? 0 : 2) + (entry.state === 'blocked' ? 1 : 0);
const open = of('backlog')
  .filter(([, entry]) => entry.state === 'open' || entry.state === 'blocked')
  .sort(([a, x], [b, y]) => rank(x) - rank(y) || a.localeCompare(b));

console.log('\nBacklog');
console.log(
  table(
    open.map(([id, entry]) => {
      const blockers = targets(entry, 'blocked-by');
      return [entry.priority ?? '?', entry.state, id, blockers.length > 0 ? `← ${blockers.join(', ')}` : ''];
    }),
  ),
);

// Everything named as produced or blocking that has no change yet: the work the
// registry implies and nothing has scheduled.
const pending = new Map();
for (const [id, entry] of entries)
  for (const { target } of relationsOf(entry))
    if (isChangeTarget(target) && !changeExists(changeName(target)))
      pending.set(changeName(target), (pending.get(changeName(target)) ?? new Set()).add(id));

console.log('\nChanges named but not created');
console.log(
  table(
    [...pending.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([id, sources]) => [id, `wanted by ${[...sources].sort().join(', ')}`]),
  ),
);

const closed = entries.filter(([, entry]) => entry.state === 'closed').length;
const retired = entries.filter(([, entry]) => entry.state === 'superseded' || entry.state === 'refuted').length;
console.log(
  `\n${entries.length} artifacts, ${closed} closed, ${retired} superseded or refuted. ` +
    `${changes.active.size} active change${changes.active.size === 1 ? '' : 's'}, ${changes.archived.size} archived.\n`,
);
