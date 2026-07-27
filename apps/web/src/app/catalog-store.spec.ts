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
    store.addPreset('spec');
    const preset = store.presets()[0];
    expect(store.addEntry(preset.id, 'parent').ok).toBe(true);
    expect(store.addEntry(preset.id, 'child').ok).toBe(true);

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
    store.addPreset('spec');
    const preset = store.presets()[0];
    expect(store.eligibleFields(preset).map(field => field.name)).toEqual(['speculative']);
    store.addEntry(preset.id, 'speculative');
    expect(store.eligibleFields(store.presets()[0]).map(field => field.name)).toContain('draft-model');
  });

  it('protects a preset parent entry while configured children remain', () => {
    seed([
      { name: 'speculative', kind: 'void' },
      { name: 'draft-model', kind: 'string', parent: 'speculative' },
    ]);
    store.addPreset('spec');
    const preset = store.presets()[0];
    store.addEntry(preset.id, 'speculative');
    store.addEntry(preset.id, 'draft-model');
    expect(store.removeEntry(preset.id, 'speculative').ok).toBe(false);
  });
});
