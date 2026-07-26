import { KeyValuePipe } from '@angular/common';
import { Component, inject, Input, signal } from '@angular/core';
import { ModelRecord, ModelService } from './model.service';

@Component({
  selector: 'app-model-metadata',
  imports: [KeyValuePipe],
  templateUrl: './model-metadata.component.html',
  styleUrl: './model-metadata.component.scss',
})
export class ModelMetadataComponent {
  private readonly models = inject(ModelService);
  @Input({ required: true })
  repository = '';
  @Input({ required: true })
  filename = '';
  @Input()
  reference = '';
  readonly record = signal<ModelRecord | null>(null);
  readonly expanded = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');

  async toggle() {
    if (this.record()) {
      this.expanded.update(value => !value);
      return;
    }
    this.loading.set(true);
    this.error.set('');
    try {
      const record = this.reference
        ? await this.models.inspectRemote(this.reference, this.filename)
        : await this.models.installedMetadata(this.repository, this.filename);
      this.record.set(record);
      this.expanded.set(true);
    } catch {
      this.error.set('Unable to load GGUF metadata.');
    } finally {
      this.loading.set(false);
    }
  }

  format(value: unknown): string {
    if (value === null) return 'null';
    if (typeof value === 'string') return value;
    if (typeof value === 'number' || typeof value === 'boolean') return String(value);
    if (typeof value === 'object' && !Array.isArray(value)) {
      const summary = value as { count?: unknown; omitted?: unknown; };
      if (summary.omitted === true && typeof summary.count === 'number') return `Array (${summary.count.toLocaleString()} items)`;
    }
    return JSON.stringify(value);
  }
}
