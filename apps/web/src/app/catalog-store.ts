import { computed, Injectable, signal } from '@angular/core';

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
  allowsChildren?: boolean;
};
export type PresetEntry = { fieldId: string; value?: FieldValue; };
export type Preset = { id: string; name: string; entries: Array<PresetEntry>; };
export type StoreResult = { ok: true; } | { ok: false; message: string; };

@Injectable({ providedIn: 'root' })
export class FieldCatalogStore {
  private readonly fieldsState = signal<Array<FieldDefinition>>([]);
  private readonly presetsState = signal<Array<Preset>>([]);
  readonly fields = this.fieldsState.asReadonly();
  readonly presets = this.presetsState.asReadonly();
  readonly fieldCount = computed(() => this.fieldsState().length);

  addField(input: Omit<FieldDefinition, 'id'>): StoreResult {
    const result = this.validateField(input);
    if (!result.ok) return result;
    this.fieldsState.update(fields => [...fields, { ...input, id: crypto.randomUUID() }]);
    return { ok: true };
  }

  addDraft(kind: FieldKind, parentId?: string): string {
    const id = crypto.randomUUID();
    this.fieldsState.update(fields => [...fields, { id, name: '', kind, draft: true, ...(parentId ? { prerequisite: { parentId } } : {}) }]);
    return id;
  }

  updateField(id: string, input: Omit<FieldDefinition, 'id'>): StoreResult {
    const current = this.fieldsState().find(field => field.id === id);
    if (!current) return { ok: false, message: 'Field not found.' };
    if (current.kind !== input.kind && this.isReferenced(id)) {
      return { ok: false, message: 'Remove preset entries before changing a referenced field kind.' };
    }
    if (current.allowsChildren && input.allowsChildren === false && this.fieldsState().some(field => field.prerequisite?.parentId === id)) {
      return { ok: false, message: 'Remove child fields before disabling child creation.' };
    }
    const result = current.draft && !input.name.trim() ? { ok: true as const } : this.validateField(input, id);
    if (!result.ok) return result;
    this.fieldsState.update(fields => fields.map(field => field.id === id ? { ...input, id, draft: !input.name.trim() } : field));
    return { ok: true };
  }

  deleteField(id: string): StoreResult {
    if (!this.fieldsState().some(field => field.id === id)) return { ok: false, message: 'Field not found.' };
    const removed = new Set<string>();
    const collect = (fieldId: string): void => {
      removed.add(fieldId);
      this.fieldsState().filter(field => field.prerequisite?.parentId === fieldId).forEach(field => collect(field.id));
    };
    collect(id);
    this.fieldsState.update(fields => fields.filter(field => !removed.has(field.id)));
    this.presetsState.update(presets => presets.map(preset => ({ ...preset, entries: preset.entries.filter(entry => !removed.has(entry.fieldId)) })));
    return { ok: true };
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

  private updatePreset(id: string, entries: Array<PresetEntry>): void {
    this.presetsState.update(presets => presets.map(preset => preset.id === id ? { ...preset, entries } : preset));
  }
  private isReferenced(id: string): boolean {
    return this.presetsState().some(preset => preset.entries.some(entry => entry.fieldId === id));
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
  private validateField(input: Omit<FieldDefinition, 'id'>, replacing?: string): StoreResult {
    if (!/^\S+$/.test(input.name) || input.name.includes('=')) return { ok: false, message: 'Field names must not contain spaces or =.' };
    if (this.fieldsState().some(field => field.name === input.name && field.id !== replacing)) return { ok: false, message: 'Field names must be unique.' };
    if (input.kind === 'enum' && (!input.enumOptions?.length || new Set(input.enumOptions).size !== input.enumOptions.length)) {
      return { ok: false, message: 'Enum options must be non-empty and distinct.' };
    }
    if (input.kind === 'void' && input.defaultValue !== undefined) return { ok: false, message: 'Void fields cannot have values.' };
    if (input.prerequisite && !this.fieldsState().some(field => field.id === input.prerequisite!.parentId && field.id !== replacing)) {
      return { ok: false, message: 'Choose an existing parent field.' };
    }
    return { ok: true };
  }
}
