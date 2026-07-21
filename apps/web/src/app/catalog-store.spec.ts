import { FieldCatalogStore } from './catalog-store';

describe('FieldCatalogStore', () => {
  let store: FieldCatalogStore;
  beforeEach(() => store = new FieldCatalogStore());

  it('creates typed fields and rejects duplicate names', () => {
    expect(store.addField({ name: '--ctx-size', kind: 'integer', defaultValue: 4096 })).toEqual({ ok: true });
    expect(store.addField({ name: '--ctx-size', kind: 'integer' }).ok).toBe(false);
  });

  it('requires distinct enum options', () => {
    expect(store.addField({ name: '--cache', kind: 'enum', enumOptions: ['q8', 'q8'] }).ok).toBe(false);
    expect(store.addField({ name: '--cache', kind: 'enum', enumOptions: ['q8', 'f16'] }).ok).toBe(true);
  });

  it('only makes a dependent field eligible after its void parent exists', () => {
    store.addField({ name: '--speculative', kind: 'void' });
    const parent = store.fields()[0];
    store.addField({ name: '--draft-model', kind: 'string', prerequisite: { parentId: parent.id } });
    store.addPreset('spec');
    const preset = store.presets()[0];
    expect(store.eligibleFields(preset).map((field) => field.name)).toEqual(['--speculative']);
    store.addEntry(preset.id, parent.id);
    expect(store.eligibleFields(store.presets()[0]).map((field) => field.name)).toContain('--draft-model');
  });

  it('protects a preset parent entry while configured children remain', () => {
    store.addField({ name: '--speculative', kind: 'void' });
    const parent = store.fields()[0];
    store.addField({ name: '--draft-model', kind: 'string', prerequisite: { parentId: parent.id } });
    const child = store.fields()[1];
    store.addPreset('spec'); const preset = store.presets()[0];
    store.addEntry(preset.id, parent.id); store.addEntry(preset.id, child.id);
    expect(store.removeEntry(preset.id, parent.id).ok).toBe(false);
  });

  it('adds an eligible string child and retains its concrete preset value', () => {
    store.addField({ name: 'fit', kind: 'void' });
    const parent = store.fields()[0];
    store.addField({ name: 'fit-target', kind: 'string', prerequisite: { parentId: parent.id } });
    const child = store.fields()[1];
    store.addPreset('fit preset');
    const preset = store.presets()[0];
    expect(store.addEntry(preset.id, parent.id).ok).toBe(true);
    expect(store.eligibleFields(store.presets()[0]).map((field) => field.name)).toEqual(['fit-target']);
    expect(store.addEntry(preset.id, child.id).ok).toBe(true);
    expect(store.updateEntry(preset.id, child.id, 'MiB0').ok).toBe(true);
    expect(store.presets()[0].entries.find((entry) => entry.fieldId === child.id)?.value).toBe('MiB0');
  });

  it('removes a field subtree and its preset entries', () => {
    store.addField({ name: 'fit', kind: 'void', allowsChildren: true });
    const parent = store.fields()[0];
    store.addField({ name: 'fit-target', kind: 'string', prerequisite: { parentId: parent.id } });
    const child = store.fields()[1];
    store.addPreset('fit preset'); const preset = store.presets()[0];
    store.addEntry(preset.id, parent.id); store.addEntry(preset.id, child.id);
    expect(store.deleteField(parent.id).ok).toBe(true);
    expect(store.fields()).toEqual([]);
    expect(store.presets()[0].entries).toEqual([]);
  });
});
