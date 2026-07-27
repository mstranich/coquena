import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FieldCatalogStore } from './catalog-store';

describe('FieldCatalogStore', () => {
  let store: FieldCatalogStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [FieldCatalogStore, provideHttpClient(), provideHttpClientTesting()] });
    store = TestBed.inject(FieldCatalogStore);
    http = TestBed.inject(HttpTestingController);
  });

  function seed(fields: Array<{ name: string; kind: string; default?: unknown; parent?: string; parentValue?: unknown; }>): void {
    store.load();
    http.expectOne('/api/fields').flush({ schemaVersion: 1, result: { schemaVersion: 1, fields } });
  }

  type ApiValue = { field: string; value?: unknown; source: 'own' | 'global'; };
  function definePreset(name: string, values: Array<ApiValue> = []): void {
    store.addPreset(name);
    http.expectOne('/api/presets').flush({ schemaVersion: 1, result: { name, kind: 'model', values } });
    http.expectOne('/api/presets').flush({
      schemaVersion: 1,
      result: { global: { name: 'global', kind: 'global', values: [] }, presets: [{ name, kind: 'model', values }] },
    });
  }
  function putValue(name: string, field: string, values: Array<ApiValue>): void {
    http.expectOne(`/api/presets/${name}/values/${field}`).flush({ schemaVersion: 1, result: { field } });
    http.expectOne('/api/presets').flush({
      schemaVersion: 1,
      result: { global: { name: 'global', kind: 'global', values: [] }, presets: [{ name, kind: 'model', values }] },
    });
  }

  it('loads the catalog from the API', () => {
    seed([{ name: 'ctx-size', kind: 'integer', default: 4096 }]);
    expect(store.fields().map(field => field.name)).toEqual(['ctx-size']);
  });

  it('persists a new field by defining it through the API', () => {
    const id = store.addDraft('integer');
    store.save(id, { name: 'ctx-size', kind: 'integer', defaultValue: 4096 });
    const request = http.expectOne('/api/fields');
    expect(request.request.method).toBe('POST');
    request.flush({ schemaVersion: 1, result: { name: 'ctx-size', kind: 'integer', default: 4096 } });
    expect(store.fields().map(field => field.id)).toEqual(['ctx-size']);
    expect(store.fields()[0].draft).toBe(false);
  });

  it('does not call the API when a draft has no name yet', () => {
    const id = store.addDraft('integer');
    store.save(id, { name: '', kind: 'integer' });
    http.expectNone('/api/fields');
  });

  it('survives a reload', () => {
    seed([{ name: 'ctx-size', kind: 'integer', default: 4096 }]);
    store.load();
    http.expectOne('/api/fields').flush({ schemaVersion: 1, result: { schemaVersion: 1, fields: [{ name: 'ctx-size', kind: 'integer', default: 4096 }] } });
    expect(store.fields().map(field => field.name)).toEqual(['ctx-size']);
  });

  it('renders a rejected save without discarding the previously loaded catalog', () => {
    seed([{ name: 'ctx-size', kind: 'integer', default: 4096 }]);
    const id = store.addDraft('string');
    store.save(id, { name: 'ctx-size', kind: 'string' });
    http.expectOne('/api/fields').flush({ error: 'field "ctx-size" already exists' }, { status: 400, statusText: 'Bad Request' });
    expect(store.error()).toBe('field "ctx-size" already exists');
    expect(store.fields().map(field => field.name)).toContain('ctx-size');
    expect(store.fields().find(field => field.draft)).toBeTruthy();
  });

  it('updates an existing field through PUT', () => {
    seed([{ name: 'ctx-size', kind: 'integer', default: 4096 }]);
    const field = store.fields()[0];
    store.save(field.id, { name: 'ctx-size', kind: 'integer', defaultValue: 8192 });
    const request = http.expectOne('/api/fields/ctx-size');
    expect(request.request.method).toBe('PUT');
    request.flush({ schemaVersion: 1, result: { name: 'ctx-size', kind: 'integer', default: 8192 } });
    expect(store.fields()[0].defaultValue).toBe(8192);
  });

  it('deletes a field and its cascaded preset entries', () => {
    seed([
      { name: 'parent', kind: 'void' },
      { name: 'child', kind: 'string', parent: 'parent' },
    ]);
    definePreset('spec');
    const preset = store.presets()[0];
    expect(store.addEntry(preset.name, 'parent').ok).toBe(true);
    putValue('spec', 'parent', [{ field: 'parent', value: true, source: 'own' }]);

    store.deleteField('parent');
    const request = http.expectOne('/api/fields/parent');
    expect(request.request.method).toBe('DELETE');
    request.flush({ schemaVersion: 1, result: { schemaVersion: 1, fields: [] } });

    expect(store.fields()).toEqual([]);
    expect(store.presets()[0].entries).toEqual([]);
  });

  it('only makes a dependent field eligible after its void parent exists', () => {
    seed([
      { name: 'speculative', kind: 'void' },
      { name: 'draft-model', kind: 'string', parent: 'speculative' },
    ]);
    definePreset('spec');
    const preset = store.presets()[0];
    expect(store.eligibleFields(preset).map(field => field.name)).toEqual(['speculative']);
    store.addEntry(preset.name, 'speculative');
    putValue('spec', 'speculative', [{ field: 'speculative', value: true, source: 'own' }]);
    expect(store.eligibleFields(store.presets()[0]).map(field => field.name)).toContain('draft-model');
  });

  it('protects a preset parent entry while configured children remain', () => {
    seed([
      { name: 'speculative', kind: 'void' },
      { name: 'draft-model', kind: 'string', parent: 'speculative' },
    ]);
    definePreset('spec');
    const preset = store.presets()[0];
    store.addEntry(preset.name, 'speculative');
    putValue('spec', 'speculative', [{ field: 'speculative', value: true, source: 'own' }]);
    store.addEntry(preset.name, 'draft-model');
    putValue('spec', 'draft-model', [
      { field: 'speculative', value: true, source: 'own' },
      { field: 'draft-model', value: undefined, source: 'own' },
    ]);
    expect(store.removeEntry(preset.name, 'speculative').ok).toBe(false);
  });

  it('reports an effective value labeled by its inheritance source', () => {
    definePreset('spec');
    store.loadPresets();
    http.expectOne('/api/presets').flush({
      schemaVersion: 1,
      result: { global: { name: 'global', kind: 'global', values: [] }, presets: [{ name: 'spec', kind: 'model', values: [{ field: 'temp', value: '0.7', source: 'global' }] }] },
    });
    expect(store.presets()[0].entries).toEqual([{ fieldId: 'temp', value: '0.7', source: 'global' }]);
  });

  it('renders a rejected preset mutation without discarding the preset', () => {
    definePreset('spec');
    store.deletePreset('spec');
    const request = http.expectOne('/api/presets/spec');
    expect(request.request.method).toBe('DELETE');
    request.flush({ error: 'preset not found' }, { status: 404, statusText: 'Not Found' });
    http.expectNone('/api/presets');
    expect(store.error()).toBe('preset not found');
    expect(store.presets().map(preset => preset.name)).toContain('spec');
  });
});
