import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FieldCatalogStore, FieldDefinition, FieldKind } from './catalog-store';
import { IconComponent } from './icon.component';

type Editor = { name: string; defaultValue: string; enumInput: string; enumOptions: Array<string>; falseArgument: string; };

@Component({
  selector: 'app-fields',
  imports: [FormsModule, IconComponent, NgTemplateOutlet],
  templateUrl: './fields.component.html',
  styleUrl: './workspace.scss',
})
export class FieldsComponent implements OnInit {
  readonly filter = signal('');
  readonly message = signal('');
  private readonly editors = new Map<string, Editor>();
  private readonly childrenExpanded = new Set<string>();
  constructor(readonly store: FieldCatalogStore) {}
  ngOnInit(): void {
    this.store.load();
  }
  get roots(): Array<FieldDefinition> {
    return this.store.fields().filter(field => !field.prerequisite && this.matchesTree(field));
  }
  children(parentId: string): Array<FieldDefinition> {
    return this.store.fields().filter(field => field.prerequisite?.parentId === parentId && this.matchesTree(field));
  }
  showChildren(field: FieldDefinition): boolean {
    return this.store.hasChildren(field.id) || this.childrenExpanded.has(field.id);
  }
  editor(field: FieldDefinition): Editor {
    let editor = this.editors.get(field.id);
    if (!editor) {
      editor = {
        name: field.name,
        defaultValue: field.defaultValue?.toString() ?? '',
        enumInput: '',
        enumOptions: [...(field.enumOptions ?? [])],
        falseArgument: field.falseArgument ?? '',
      };
      this.editors.set(field.id, editor);
    }
    return editor;
  }
  add(kind: FieldKind, parentId?: string): void {
    const id = this.store.addDraft(kind, parentId);
    this.editors.delete(id);
    this.message.set('');
    setTimeout(() => document.getElementById(`field-${id}`)?.focus());
  }
  addChild(field: FieldDefinition, kind: FieldKind): void {
    this.add(kind, field.id);
  }
  addOption(editor: Editor): void {
    const value = editor.enumInput.trim();
    if (value && !editor.enumOptions.includes(value)) editor.enumOptions.push(value);
    editor.enumInput = '';
  }
  removeOption(editor: Editor, value: string): void {
    editor.enumOptions = editor.enumOptions.filter(item => item !== value);
  }
  save(field: FieldDefinition): void {
    const editor = this.editor(field);
    const value = field.kind === 'integer' && editor.defaultValue
      ? Number(editor.defaultValue)
      : field.kind === 'boolean'
      ? editor.defaultValue === 'true'
      : editor.defaultValue || undefined;
    const name = field.draft ? editor.name.trim() : field.name;
    const input = {
      name,
      kind: field.kind,
      ...(field.kind === 'void' ? {} : { defaultValue: value }),
      ...(field.kind === 'enum' ? { enumOptions: editor.enumOptions } : {}),
      ...(editor.falseArgument.trim() ? { falseArgument: editor.falseArgument.trim() } : {}),
      ...(field.prerequisite ? { prerequisite: field.prerequisite } : {}),
    };
    if (field.draft && !name) {
      this.message.set('Enter a name to finish this draft.');
      return;
    }
    this.message.set('');
    this.store.save(field.id, input);
    if (field.draft) this.editors.delete(field.id);
  }
  remove(field: FieldDefinition): void {
    this.store.deleteField(field.id);
  }
  enableChildren(field: FieldDefinition): void {
    this.childrenExpanded.add(field.id);
  }
  private matchesTree(field: FieldDefinition): boolean {
    const query = this.filter().toLowerCase();
    return !query || field.draft || field.name.toLowerCase().includes(query)
      || this.store.fields().some(child => child.prerequisite?.parentId === field.id && this.matchesTree(child));
  }
}
