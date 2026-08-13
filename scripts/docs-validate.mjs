// Validates docs/registry.yaml and every reference it declares.
//
// The registry is the single source of truth for how stories, findings, use
// cases, decisions, backlog entries and RFCs relate to each other and to
// OpenSpec changes.
// Nothing else stores those links, so nothing else can contradict them — but
// only if something checks that they resolve. That is this script's whole job,
// and it is why it exits non-zero: a dangling reference should fail a
// verification run rather than wait to be noticed.
//
// Run with `task docs:validate`.

import {
  CHANGE_PREFIX,
  KINDS,
  RELATIONS,
  changeName,
  isChangeTarget,
  loadArtifacts,
  loadChanges,
  relationsOf,
} from './docs-registry.mjs';

const problems = [];
const notes = [];
const report = (where, message) => problems.push(`${where}: ${message}`);
const note = (where, message) => notes.push(`${where}: ${message}`);

const REQUIRED = ['kind', 'state', 'title', 'path', 'openedAt', 'owner'];

const { where: registryFile, artifacts, files } = await loadArtifacts();
const changes = await loadChanges();
const changeExists = (id) => changes.active.has(id) || changes.archived.has(id);

const declared = new Set();

for (const [id, entry] of Object.entries(artifacts)) {
  const at = `${registryFile} → ${id}`;
  if (entry == null || typeof entry !== 'object' || Array.isArray(entry)) {
    report(at, 'entry is not a mapping');
    continue;
  }

  for (const field of REQUIRED)
    if (entry[field] == null) report(at, `missing required field "${field}"`);

  const kind = KINDS[entry.kind];
  if (entry.kind != null && kind == null)
    report(at, `unknown kind "${entry.kind}" (expected ${Object.keys(KINDS).join(', ')})`);

  // Each kind carries its own vocabulary: "refuted" is meaningless for a
  // backlog entry, and "blocked" is meaningless for a finding.
  if (kind != null && entry.state != null && !kind.states.includes(entry.state))
    report(at, `state "${entry.state}" is not valid for kind "${entry.kind}" (expected ${kind.states.join(', ')})`);

  if (typeof entry.path === 'string') {
    declared.add(entry.path);
    if (!files.includes(entry.path)) report(at, `path "${entry.path}" does not exist`);
    // Location encodes lifecycle, the way OpenSpec archives a change by moving
    // it. The two must agree or one of them is lying.
    const archived = entry.path.includes('/archive/');
    if (archived && entry.state !== 'closed')
      report(at, `lives under archive/ but state is "${entry.state}"`);
    if (!archived && entry.state === 'closed')
      report(at, 'state is "closed" but the document is not under archive/');
  }

  if (kind?.closes === false && entry.closedAt != null)
    report(at, `kind "${entry.kind}" does not close, so closedAt must be null`);
  if (entry.state === 'closed' && entry.closedAt == null)
    report(at, 'state is "closed" but closedAt is null');
  if (entry.state !== 'closed' && entry.closedAt != null)
    report(at, `state is "${entry.state}" but closedAt is set`);

  let blockers = 0;
  for (const { type, target } of relationsOf(entry)) {
    if (!RELATIONS.includes(type)) {
      report(at, `unknown relation type "${type}" (expected ${RELATIONS.join(', ')})`);
      continue;
    }
    if (typeof target !== 'string' || target === '') {
      report(at, `relation "${type}" has no target`);
      continue;
    }
    if (type === 'blocked-by') blockers += 1;
    if (target === id) {
      report(at, `relation "${type}" points at itself`);
      continue;
    }

    if (!isChangeTarget(target)) {
      if (target in artifacts) continue;
      report(at, `${type} "${target}" is not a known artifact (prefix "${CHANGE_PREFIX}" for an OpenSpec change)`);
      continue;
    }

    if (changeExists(changeName(target))) continue;
    // A closed artifact claiming it produced something is a statement about
    // work that happened, so the target must exist. An open one may still be
    // describing a plan.
    if (type === 'produces' && entry.state === 'closed')
      report(at, `produces "${target}" but no such OpenSpec change exists`);
    else note(at, `${type} "${target}" does not exist yet`);
  }

  if (entry.state === 'blocked' && blockers === 0)
    report(at, 'state is "blocked" but no blocked-by relation is declared');
  if (entry.state === 'superseded' && !relationsOf(entry).some((r) => r.type === 'supersedes'))
    note(at, 'state is "superseded"; the replacing artifact should declare supersedes');
}

for (const file of files)
  if (!declared.has(file) && file !== 'registry.yaml')
    report(registryFile, `"${file}" exists but no artifact declares it`);

const counted = (n, singular, plural = `${singular}s`) => `${n} ${n === 1 ? singular : plural}`;
const byKind = Object.entries(KINDS)
  .map(([kind]) => [kind, Object.values(artifacts).filter((entry) => entry?.kind === kind).length])
  .filter(([, count]) => count > 0)
  .map(([kind, count]) => `${count} ${kind}`)
  .join(', ');

console.log(
  `docs: ${counted(Object.keys(artifacts).length, 'artifact')} (${byKind}), ` +
    `${counted(changes.active.size, 'active change')}, ${changes.archived.size} archived`,
);
for (const line of notes) console.log(`  note  ${line}`);
if (problems.length > 0) {
  for (const line of problems) console.error(`  error ${line}`);
  console.error(`\n${counted(problems.length, 'problem')} found.`);
  process.exitCode = 1;
} else {
  console.log('  all references resolve.');
}
