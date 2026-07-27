import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
export interface ModelCandidate {
  source: { repository: string; filename: string; };
  artifact: { sizeBytes: number; };
}
export interface ModelJob {
  id: string;
  source: { repository: string; filename: string; resolvedRevision: string; };
  targetPath: string;
  status: string;
  downloadedBytes: number;
  expectedSizeBytes: number;
  error?: string;
}
/** The compact digest the backend derives from a GGUF header. It replaces the
 * raw metadata document the API no longer returns: the vocabulary arrays never
 * leave the backend, so nothing here can grow unbounded. */
export interface ModelClassification {
  type: string;
  architecture?: string;
  sizeLabel?: string;
  contextLength?: number;
  embeddingLength?: number;
  projectionDim?: number;
  projectorType?: string;
}
/** An inspection: the artifact's identity, its classification and the scalar
 * metadata keys only. */
export interface ModelInspection {
  source: { repository: string; filename: string; resolvedRevision: string; };
  classification: ModelClassification;
  metadata: Record<string, unknown>;
}
export interface ModelLibraryEntry {
  source: { repository: string; filename: string; resolvedRevision: string; };
  targetPath: string;
  state: string;
  classification?: ModelClassification;
  job?: ModelJob;
}

@Injectable({ providedIn: 'root' })
export class ModelService {
  private readonly http = inject(HttpClient);
  private readonly streams = new Map<string, EventSource>();
  readonly candidates = signal<Array<ModelCandidate>>([]);
  readonly library = signal<Array<ModelLibraryEntry>>([]);
  readonly error = signal('');
  readonly loading = signal(false);
  async loadLibrary() {
    try {
      const value = await firstValueFrom(this.http.get<{ result: Array<ModelLibraryEntry>; }>('/api/models/library'));
      this.library.set(value.result);
    } catch (error) {
      this.error.set(this.apiError(error, 'Unable to load the model library.'));
    }
  }
  async resolve(reference: string) {
    this.loading.set(true);
    this.error.set('');
    try {
      const v = await firstValueFrom(this.http.post<{ result: Array<ModelCandidate>; }>('/api/models/resolve', { reference }));
      this.candidates.set(v.result);
    } catch (error) {
      this.error.set(this.apiError(error, 'Unable to resolve GGUF artifacts.'));
    } finally {
      this.loading.set(false);
    }
  }
  async inspectRemote(reference: string, selector: string): Promise<ModelInspection> {
    const value = await firstValueFrom(
      this.http.post<{ result: ModelInspection; }>('/api/models/inspect', { reference, selector }),
    );
    return value.result;
  }
  async installedMetadata(repository: string, filename: string): Promise<ModelInspection> {
    const value = await firstValueFrom(
      this.http.post<{ result: ModelInspection; }>('/api/models/metadata', { repository, filename }),
    );
    return value.result;
  }
  publisher(repository: string): string {
    return repository.split('/')[0];
  }
  remoteAvatar(repository: string): string {
    return 'https://huggingface.co/api/avatars/' + encodeURIComponent(this.publisher(repository));
  }
  localAvatar(repository: string): string {
    return '/api/models/publishers/' + encodeURIComponent(this.publisher(repository)) + '/avatar';
  }
  async download(reference: string, selector: string) {
    this.error.set('');
    try {
      const v = await firstValueFrom(this.http.post<{ result: ModelJob; }>('/api/models/downloads', { reference, selector }));
      this.updateJob(v.result);
      this.events(v.result.id);
    } catch (error) {
      this.error.set(this.apiError(error, 'Unable to start model download.'));
    }
  }
  private events(id: string) {
    this.streams.get(id)?.close();
    const events = new EventSource('/api/models/downloads/' + id + '/events');
    this.streams.set(id, events);
    events.onmessage = event => {
      this.updateJob(JSON.parse(event.data) as ModelJob);
    };
    for (const name of ['progress', 'paused', 'completed', 'failed']) {
      events.addEventListener(name, event => {
        this.updateJob(JSON.parse((event as MessageEvent).data) as ModelJob);
        if (name !== 'progress') this.closeEvents(id, events);
        if (name === 'completed' || name === 'paused' || name === 'failed') void this.loadLibrary();
      });
    }
    events.onerror = () => {
      this.closeEvents(id, events);
      this.error.set('Unable to read model download status.');
    };
  }
  async action(job: ModelJob, action: 'pause' | 'resume' | 'cancel') {
    try {
      const v = await firstValueFrom(this.http.post<{ result: ModelJob; }>('/api/models/downloads/' + job.id + '/' + action, {}));
      this.updateJob(v.result);
      if (action === 'resume') this.events(job.id);
      if (action === 'cancel') {
        this.closeEvents(job.id);
        await this.loadLibrary();
      }
    } catch (error) {
      this.error.set(this.apiError(error, 'Model action failed.'));
    }
  }
  private updateJob(job: ModelJob) {
    this.library.update(entries => {
      const index = entries.findIndex(entry => entry.job?.id === job.id || (job.targetPath && entry.targetPath === job.targetPath));
      const entry: ModelLibraryEntry = {
        source: job.source,
        targetPath: job.targetPath,
        state: job.status,
        job,
      };
      if (index < 0) return [...entries, entry];
      return entries.map((current, position) => position === index ? { ...current, ...entry } : current);
    });
  }
  private closeEvents(id: string, expected?: EventSource) {
    const stream = this.streams.get(id);
    if (!stream || (expected && stream !== expected)) return;
    stream.close();
    this.streams.delete(id);
  }
  private apiError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && typeof error.error?.error === 'string') return error.error.error;
    return fallback;
  }
}
