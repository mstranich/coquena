import { HttpClient } from '@angular/common/http';
import { computed, Injectable, signal } from '@angular/core';
import { catchError, finalize, of } from 'rxjs';

export type FieldKind = 'string' | 'integer' | 'boolean' | 'void' | 'enum';
export type FieldValue = string | number | boolean;
export type Prerequisite = { parentId: string; value?: FieldValue; };
export type FieldDefinition = {
  id: string;
  name: string;
  kind: FieldKind;
  defaultValue?: FieldValue;
  enumOptions?: Array<string>;
  falseArgument?: string;
  prerequisite?: Prerequisite;
  draft?: boolean;
};
export type PresetEntry = { fieldId: string; value?: FieldValue; };
export type Preset = { id: string; name: string; entries: Array<PresetEntry>; };
export type StoreResult = { ok: true; } | { ok: false; message: string; };

interface Envelope<T> {
  schemaVersion: number;
  result: T;
}
interface ApiField {
  name: string;
  kind: FieldKind;
  default?: FieldValue;
  enumOptions?: Array<string>;
  falseArgument?: string;
  parent?: string;
  parentValue?: FieldValue;
}
interface ApiCatalog {
  schemaVersion: number;
  fields: Array<ApiField>;
}

function fromApi(field: ApiField): FieldDefinition {
  return {
    id: field.name,
    name: field.name,
    kind: field.kind,
    defaultValue: field.default,
    enumOptions: field.enumOptions,
    falseArgument: field.falseArgument,
    prerequisite: field.parent ? { parentId: field.parent, value: field.parentValue } : undefined,
    draft: false,
  };
}
function toApi(name: string, input: Omit<FieldDefinition, 'id' | 'draft'>): ApiField {
  return {
    name,
    kind: input.kind,
    ...(input.defaultValue !== undefined ? { default: input.defaultValue } : {}),
    ...(input.enumOptions ? { enumOptions: input.enumOptions } : {}),
    ...(input.falseArgument ? { falseArgument: input.falseArgument } : {}),
    ...(input.prerequisite
      ? { parent: input.prerequisite.parentId, ...(input.prerequisite.value !== undefined ? { parentValue: input.prerequisite.value } : {}) }
      : {}),
  };
}

@Injectable({ providedIn: 'root' })
export class FieldCatalogStore {
  private readonly fieldsState = signal<Array<FieldDefinition>>([]);
  private readonly presetsState = signal<Array<Preset>>([]);
  readonly fields = this.fieldsState.asReadonly();
  readonly presets = this.presetsState.asReadonly();
  readonly fieldCount = computed(() => this.fieldsState().length);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  constructor(private readonly http: HttpClient) {}

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.http.get<Envelope<ApiCatalog>>('/api/fields').pipe(
      finalize(() => this.loading.set(false)),
      catchError(error => {
        this.error.set(this.message(error));
        return of(null);
      }),
    ).subscribe(value => {
      if (!value) return;
      const persisted = value.result.fields.map(fromApi);
      this.fieldsState.update(current => [...persisted, ...current.filter(field => field.draft)]);
    });
  }

  addDraft(kind: FieldKind, parentId?: string): string {
    const id = crypto.randomUUID();
    this.fieldsState.update(fields => [...fields, { id, name: '', kind, draft: true, ...(parentId ? { prerequisite: { parentId } } : {}) }]);
    return id;
  }

  save(id: string, input: Omit<FieldDefinition, 'id' | 'draft'>): void {
    const current = this.fieldsState().find(field => field.id === id);
    if (!current) {
      this.error.set('Field not found.');
      return;
    }
    if (current.draft && !input.name.trim()) return;
    if (current.kind !== input.kind) {
      this.error.set('Field kind cannot change once created.');
      return;
    }
    this.error.set(null);
    const name = current.draft ? input.name.trim() : current.name;
    const payload = toApi(name, input);
    const request = current.draft
      ? this.http.post<Envelope<ApiField>>('/api/fields', payload)
      : this.http.put<Envelope<ApiField>>(`/api/fields/${encodeURIComponent(current.name)}`, payload);
    request.pipe(
      catchError(error => {
        this.error.set(this.message(error));
        return of(null);
      }),
    ).subscribe(value => {
      if (!value) return;
      const saved = fromApi(value.result);
      this.fieldsState.update(fields => fields.map(field => field.id === id ? { ...saved, id: saved.name } : field));
    });
  }

  deleteField(id: string): void {
    const current = this.fieldsState().find(field => field.id === id);
    if (!current) {
      this.error.set('Field not found.');
      return;
    }
    if (current.draft) {
      this.fieldsState.update(fields => fields.filter(field => field.id !== id));
      return;
    }
    this.error.set(null);
    this.http.delete<Envelope<ApiCatalog>>(`/api/fields/${encodeURIComponent(current.name)}`).pipe(
      catchError(error => {
        this.error.set(this.message(error));
        return of(null);
      }),
    ).subscribe(value => {
      if (!value) return;
      const remainingNames = new Set(value.result.fields.map(field => field.name));
      this.fieldsState.update(fields => fields.filter(field => field.draft || remainingNames.has(field.name)));
      this.presetsState.update(presets => presets.map(preset => ({ ...preset, entries: preset.entries.filter(entry => remainingNames.has(entry.fieldId)) })));
    });
  }

  addPreset(name: string): StoreResult {
    if (!name.trim() || this.presetsState().some(preset => preset.name === name.trim())) return { ok: false, message: 'Preset names must be unique.' };
    this.presetsState.update(presets => [...presets, { id: crypto.randomUUID(), name: name.trim(), entries: [] }]);
    return { ok: true };
  }

  deletePreset(id: string): void {
    this.presetsState.update(presets => presets.filter(preset => preset.id !== id));
  }

  addEntry(presetId: string, fieldId: string): StoreResult {
    const preset = this.presetsState().find(item => item.id === presetId);
    const field = this.fieldsState().find(item => item.id === fieldId);
    if (!preset || !field) return { ok: false, message: 'Preset or field not found.' };
    if (preset.entries.some(entry => entry.fieldId === fieldId)) return { ok: false, message: 'This field is already in the preset.' };
    if (!this.isEligible(field, preset)) return { ok: false, message: 'Add and activate the parent field first.' };
    const entry: PresetEntry = { fieldId, ...(field.kind === 'void' ? {} : { value: field.defaultValue }) };
    this.updatePreset(presetId, [...preset.entries, entry]);
    return { ok: true };
  }

  updateEntry(presetId: string, fieldId: string, value?: FieldValue): StoreResult {
    const preset = this.presetsState().find(item => item.id === presetId);
    const field = this.fieldsState().find(item => item.id === fieldId);
    if (!preset || !field) return { ok: false, message: 'Preset or field not found.' };
    if (!this.validValue(field, value)) return { ok: false, message: 'Value does not match the field definition.' };
    this.updatePreset(presetId, preset.entries.map(entry => entry.fieldId === fieldId ? { fieldId, ...(field.kind === 'void' ? {} : { value }) } : entry));
    return { ok: true };
  }

  removeEntry(presetId: string, fieldId: string): StoreResult {
    const preset = this.presetsState().find(item => item.id === presetId);
    if (!preset) return { ok: false, message: 'Preset not found.' };
    const children = this.fieldsState().filter(field => field.prerequisite?.parentId === fieldId).map(field => field.id);
    if (preset.entries.some(entry => children.includes(entry.fieldId))) return { ok: false, message: 'Remove dependent fields first.' };
    this.updatePreset(presetId, preset.entries.filter(entry => entry.fieldId !== fieldId));
    return { ok: true };
  }

  eligibleFields(preset: Preset): Array<FieldDefinition> {
    return this.fieldsState().filter(field => !preset.entries.some(entry => entry.fieldId === field.id) && this.isEligible(field, preset));
  }

  field(id: string): FieldDefinition | undefined {
    return this.fieldsState().find(field => field.id === id);
  }

  hasChildren(id: string): boolean {
    return this.fieldsState().some(field => field.prerequisite?.parentId === id);
  }

  private updatePreset(id: string, entries: Array<PresetEntry>): void {
    this.presetsState.update(presets => presets.map(preset => preset.id === id ? { ...preset, entries } : preset));
  }
  private isEligible(field: FieldDefinition, preset: Preset): boolean {
    if (!field.prerequisite) return true;
    const parent = preset.entries.find(entry => entry.fieldId === field.prerequisite!.parentId);
    return !!parent && (field.prerequisite.value === undefined || parent.value === field.prerequisite.value || (this.field(parent.fieldId)?.kind === 'void'));
  }
  private validValue(field: FieldDefinition, value: FieldValue | undefined): boolean {
    if (field.kind === 'void') return value === undefined;
    if (field.kind === 'integer') return typeof value === 'number' && Number.isInteger(value);
    if (field.kind === 'boolean') return typeof value === 'boolean';
    if (field.kind === 'enum') return typeof value === 'string' && !!field.enumOptions?.includes(value);
    return typeof value === 'string';
  }
  private message(error: unknown): string {
    if (typeof error === 'object' && error !== null && 'error' in error) {
      const body = (error as { error: unknown; }).error;
      if (typeof body === 'object' && body !== null && 'error' in body && typeof (body as { error: unknown; }).error === 'string') {
        return (body as { error: string; }).error;
      }
    }
    return 'Unable to update the field catalog';
  }
}
