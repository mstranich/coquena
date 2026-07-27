import { HttpClient } from '@angular/common/http';
import { computed, Injectable, signal } from '@angular/core';
import { catchError, finalize, forkJoin, map, Observable, of } from 'rxjs';

export interface LlamaRuntime {
  release: string;
  target: string;
  backend: string;
  assetId: string;
  dependencies?: Array<string>;
  compatibility?: string;
}

interface Envelope<T> {
  schemaVersion: number;
  result: T;
}
interface Operation {
  operationId: string;
}
export interface DownloadProgress {
  operationId: string;
  phase: string;
  assetId?: string;
  assetTransferredBytes?: number;
  assetTotalBytes?: number;
  totalTransferredBytes?: number;
  totalBytes?: number;
  error?: string;
}
export interface RuntimeDownload {
  id: string;
  type: 'runtime';
  name: string;
  status: string;
  downloadedBytes: number;
  expectedBytes: number;
  path: string;
  error?: string;
}

@Injectable({ providedIn: 'root' })
export class LlamacppRuntimeService {
  readonly remote = signal<Array<LlamaRuntime>>([]);
  readonly local = signal<Array<LlamaRuntime>>([]);
  readonly downloads = signal<Array<RuntimeDownload>>([]);
  readonly loading = signal(false);
  readonly mutating = signal(false);
  readonly progress = signal<DownloadProgress | null>(null);
  readonly activeDownloadId = signal<string | null>(null);
  readonly managedDownloads = computed<Array<RuntimeDownload>>(() => {
    const jobs = [...this.downloads()];
    const activeId = this.activeDownloadId();
    const active = this.progress();
    if (activeId && !jobs.some(job => job.id === activeId)) {
      jobs.push({
        id: activeId,
        type: 'runtime',
        name: activeId,
        status: active?.phase || 'queued',
        downloadedBytes: active?.totalTransferredBytes || 0,
        expectedBytes: active?.totalBytes || 0,
        path: '',
      });
    }
    return jobs;
  });
  readonly error = signal<string | null>(null);

  constructor(private readonly http: HttpClient) {}

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      remote: this.list('/api/runtimes/llama.cpp/remote'),
      local: this.list('/api/runtimes/llama.cpp'),
      downloads: this.downloadList(),
    }).pipe(
      finalize(() => this.loading.set(false)),
      catchError(error => {
        this.error.set(this.message(error));
        return of({ remote: [], local: [], downloads: [] });
      }),
    ).subscribe(({ remote, local, downloads }) => {
      this.remote.set(remote);
      this.local.set(local);
      this.downloads.set(downloads);
    });
  }

  download(assetId: string): void {
    if (this.mutating()) return;
    this.mutating.set(true);
    this.error.set(null);
    this.progress.set({ operationId: '', phase: 'queued', assetId });
    this.activeDownloadId.set(assetId);
    this.http.post<Envelope<Operation>>('/api/runtimes/llama.cpp/downloads', { assetId }).pipe(
      map(response => response.result),
      catchError(error => {
        this.finishError(this.message(error));
        return of(null);
      }),
    ).subscribe(operation => {
      if (operation) {
        this.progress.update(current => current ? { ...current, operationId: operation.operationId } : current);
        this.refreshDownloads();
        this.events(operation.operationId).subscribe({
          next: event => this.consume(event),
          error: error => this.finishError(this.message(error)),
        });
      }
    });
  }

  action(job: RuntimeDownload | DownloadProgress, action: 'pause' | 'resume' | 'cancel'): void {
    const id = 'id' in job ? job.id : job.operationId;
    if (!id || (this.mutating() && action === 'resume')) return;
    this.mutating.set(action !== 'pause');
    if (action === 'resume') this.activeDownloadId.set(id);
    this.http.post<Envelope<Operation | unknown>>(`/api/runtimes/llama.cpp/downloads/${encodeURIComponent(id)}/actions`, { action }).pipe(
      catchError(error => {
        this.finishError(this.message(error));
        return of(null);
      }),
    ).subscribe(result => {
      if (action === 'resume' && result && 'result' in result && result.result && typeof result.result === 'object' && 'operationId' in result.result) {
        this.events((result.result as Operation).operationId).subscribe({
          next: event => this.consume(event),
          error: error => this.finishError(this.message(error)),
        });
      } else if (action !== 'pause') {
        this.mutating.set(false);
        this.load();
      }
    });
  }

  remove(assetId: string): void {
    if (this.mutating() || !confirm(`Remove ${assetId}? This cannot be undone.`)) return;
    this.mutating.set(true);
    this.error.set(null);
    this.http.delete<Envelope<unknown>>(`/api/runtimes/llama.cpp/${encodeURIComponent(assetId)}`).pipe(
      finalize(() => this.mutating.set(false)),
      catchError(error => {
        this.error.set(this.message(error));
        return of(null);
      }),
    ).subscribe(result => {
      if (result) this.load();
    });
  }

  private list(url: string): Observable<Array<LlamaRuntime>> {
    return this.http.get<Envelope<Array<LlamaRuntime>>>(url).pipe(map(response => response.result || []));
  }

  private downloadList(): Observable<Array<RuntimeDownload>> {
    return this.http.get<Envelope<Array<RuntimeDownload>>>('/api/runtimes/llama.cpp/downloads').pipe(map(response => response.result || []));
  }

  private events(operationId: string): Observable<DownloadProgress> {
    return new Observable(observer => {
      const source = new EventSource(`/api/runtimes/llama.cpp/downloads/${encodeURIComponent(operationId)}/events`);
      const receive = (event: MessageEvent<string>) => {
        try {
          const progress = JSON.parse(event.data) as DownloadProgress;
          observer.next(progress);
          if (['completed', 'failed', 'paused', 'cancelled'].includes(progress.phase)) observer.complete();
        } catch {
          observer.error(new Error('Invalid runtime progress event'));
        }
      };
      source.addEventListener('progress', receive as EventListener);
      source.addEventListener('completed', receive as EventListener);
      source.addEventListener('failed', receive as EventListener);
      source.onerror = () => observer.error(new Error('Runtime progress stream disconnected'));
      return () => source.close();
    });
  }

  private consume(event: DownloadProgress): void {
    this.progress.set(event);
    if (event.phase === 'completed' || event.phase === 'paused' || event.phase === 'cancelled') {
      this.mutating.set(false);
      this.activeDownloadId.set(null);
      this.load();
    }
    if (event.phase === 'failed') this.finishError(event.error || 'Runtime download failed');
  }

  activeFor(id: string): DownloadProgress | null {
    return this.activeDownloadId() === id ? this.progress() : null;
  }

  phaseLabel(phase: string): string {
    return ({
      queued: 'Preparing download',
      downloading: 'Downloading',
      verifying: 'Verifying archive',
      extracting: 'Decompressing runtime',
      probing: 'Validating runtime',
      publishing: 'Installing runtime',
      paused: 'Paused',
      failed: 'Failed',
      cancelled: 'Cancelled',
      completed: 'Installed',
    } as Record<string, string>)[phase] || phase;
  }
  private finishError(message: string): void {
    this.error.set(message);
    this.mutating.set(false);
    this.activeDownloadId.set(null);
    this.refreshDownloads();
  }

  private refreshDownloads(): void {
    this.downloadList().pipe(catchError(() => of([]))).subscribe(downloads => this.downloads.set(downloads));
  }
  private message(error: unknown): string {
    if (typeof error === 'object' && error !== null && 'error' in error) {
      const body = error.error;
      if (typeof body === 'object' && body !== null && 'error' in body && typeof body.error === 'string') return body.error;
    }
    return error instanceof Error ? error.message : 'Runtime operation failed';
  }
}
