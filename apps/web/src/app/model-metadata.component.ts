import { Component, computed, inject, Input, signal } from '@angular/core';
import { ModelInspection, ModelService } from './model.service';

interface MetadataRow {
  key: string;
  value: string;
}

@Component({
  selector: 'app-model-metadata',
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
  readonly inspection = signal<ModelInspection | null>(null);
  readonly expanded = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');

  /** The two-column table's rows: the declared type first, so an operator reads
   * what the artifact says it is before its metadata, then the remaining
   * classification facts and the scalar metadata keys in a stable order. The
   * response carries no arrays at all, so no row can be an unbounded list. */
  readonly rows = computed<Array<MetadataRow>>(() => {
    const inspection = this.inspection();
    if (!inspection) return [];
    const classification = inspection.classification ?? { type: 'unknown' };
    const rows: Array<MetadataRow> = [{ key: 'classification.type', value: classification.type }];
    for (const key of ['architecture', 'sizeLabel', 'contextLength', 'embeddingLength', 'projectionDim', 'projectorType'] as const) {
      const value = classification[key];
      if (value !== undefined && value !== '' && value !== 0) rows.push({ key: 'classification.' + key, value: this.format(value) });
    }
    for (const key of Object.keys(inspection.metadata ?? {}).sort()) {
      rows.push({ key, value: this.format(inspection.metadata[key]) });
    }
    return rows;
  });

  async toggle() {
    if (this.inspection()) {
      this.expanded.update(value => !value);
      return;
    }
    this.loading.set(true);
    this.error.set('');
    try {
      const inspection = this.reference
        ? await this.models.inspectRemote(this.reference, this.filename)
        : await this.models.installedMetadata(this.repository, this.filename);
      this.inspection.set(inspection);
      this.expanded.set(true);
    } catch {
      this.error.set('Unable to load the GGUF classification.');
    } finally {
      this.loading.set(false);
    }
  }

  format(value: unknown): string {
    if (value === null) return 'null';
    if (typeof value === 'string') return value;
    if (typeof value === 'number' || typeof value === 'boolean') return String(value);
    return JSON.stringify(value);
  }
}
