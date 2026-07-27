import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FieldCatalogStore, FieldValue, Preset } from './catalog-store';
import { IconComponent } from './icon.component';

@Component({ selector: 'app-presets', imports: [FormsModule, IconComponent], templateUrl: './presets.component.html', styleUrl: './workspace.scss' })
export class PresetsComponent implements OnInit {
  readonly message = signal('');
  newName = '';
  constructor(readonly store: FieldCatalogStore) {}
  ngOnInit(): void {
    this.store.loadPresets();
  }
  add(): void {
    const result = this.store.addPreset(this.newName);
    this.message.set(result.ok ? 'Preset created.' : result.message);
    if (result.ok) this.newName = '';
  }
  select(preset: Preset, id: string): void {
    const result = this.store.addEntry(preset.name, id);
    this.message.set(result.ok ? '' : result.message);
  }
  setValue(preset: Preset, id: string, raw: string | boolean): void {
    const field = this.store.field(id)!;
    const value: FieldValue | undefined = field.kind === 'integer' ? Number(raw) : raw as FieldValue;
    const result = this.store.updateEntry(preset.name, id, value);
    this.message.set(result.ok ? '' : result.message);
  }
  remove(preset: Preset, id: string): void {
    const result = this.store.removeEntry(preset.name, id);
    this.message.set(result.ok ? '' : result.message);
  }
  returnToInheritance(preset: Preset, id: string): void {
    this.remove(preset, id);
  }
}
