import { Component, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FieldCatalogStore, FieldDefinition, FieldKind } from './catalog-store';
import { IconComponent } from './icon.component';

type Editor = { name: string; defaultValue: string; enumInput: string; enumOptions: string[]; falseArgument: string };

@Component({ selector: 'app-fields', imports: [FormsModule, IconComponent, NgTemplateOutlet], templateUrl: './fields.component.html', styleUrl: './workspace.scss' })
export class FieldsComponent {
  readonly filter = signal(''); readonly message = signal('');
  private readonly editors = new Map<string, Editor>();
  constructor(readonly store: FieldCatalogStore) {}
  get roots(): FieldDefinition[] { return this.store.fields().filter((field) => !field.prerequisite && this.matchesTree(field)); }
  children(parentId: string): FieldDefinition[] { return this.store.fields().filter((field) => field.prerequisite?.parentId === parentId && this.matchesTree(field)); }
  editor(field: FieldDefinition): Editor { let editor = this.editors.get(field.id); if (!editor) { editor = { name: field.name, defaultValue: field.defaultValue?.toString() ?? '', enumInput: '', enumOptions: [...(field.enumOptions ?? [])], falseArgument: field.falseArgument ?? '' }; this.editors.set(field.id, editor); } return editor; }
  add(kind: FieldKind, parentId?: string): void { const id = this.store.addDraft(kind, parentId); this.editors.delete(id); this.message.set(''); setTimeout(() => document.getElementById(`field-${id}`)?.focus()); }
  addChild(field: FieldDefinition, kind: FieldKind): void { this.add(kind, field.id); }
  addOption(editor: Editor): void { const value = editor.enumInput.trim(); if (value && !editor.enumOptions.includes(value)) editor.enumOptions.push(value); editor.enumInput = ''; }
  removeOption(editor: Editor, value: string): void { editor.enumOptions = editor.enumOptions.filter((item) => item !== value); }
  save(field: FieldDefinition): void { const editor = this.editor(field); const value = field.kind === 'integer' && editor.defaultValue ? Number(editor.defaultValue) : field.kind === 'boolean' ? editor.defaultValue === 'true' : editor.defaultValue || undefined; const input = { name: editor.name.trim(), kind: field.kind, ...(field.kind === 'void' ? {} : { defaultValue: value }), ...(field.kind === 'enum' ? { enumOptions: editor.enumOptions } : {}), ...(editor.falseArgument.trim() ? { falseArgument: editor.falseArgument.trim() } : {}), ...(field.prerequisite ? { prerequisite: field.prerequisite } : {}), allowsChildren: field.allowsChildren ?? false }; const result = this.store.updateField(field.id, input); this.message.set(result.ok ? (input.name ? 'Field saved.' : 'Enter a name to finish this draft.') : result.message); if (result.ok && input.name) this.editors.delete(field.id); }
  remove(field: FieldDefinition): void { const result = this.store.deleteField(field.id); this.message.set(result.ok ? 'Field deleted.' : result.message); }
  toggleChildren(field: FieldDefinition, event: Event): void { const enabled = (event.target as HTMLInputElement).checked; const result = this.store.updateField(field.id, { name: field.name, kind: field.kind, defaultValue: field.defaultValue, enumOptions: field.enumOptions, falseArgument: field.falseArgument, prerequisite: field.prerequisite, allowsChildren: enabled }); this.message.set(result.ok ? '' : result.message); if (!result.ok) (event.target as HTMLInputElement).checked = !enabled; }
  private matchesTree(field: FieldDefinition): boolean { const query = this.filter().toLowerCase(); return !query || field.draft || field.name.toLowerCase().includes(query) || this.store.fields().some((child) => child.prerequisite?.parentId === field.id && this.matchesTree(child)); }
}
