import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
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

@Injectable({ providedIn: 'root' })
export class LlamacppRuntimeService {
  readonly remote = signal<Array<LlamaRuntime>>([]);
  readonly local = signal<Array<LlamaRuntime>>([]);
  readonly loading = signal(false);
  readonly mutating = signal(false);
  readonly progress = signal<DownloadProgress | null>(null);
  readonly error = signal<string | null>(null);

  constructor(private readonly http: HttpClient) {}

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      remote: this.list('/api/runtimes/llama.cpp/remote'),
      local: this.list('/api/runtimes/llama.cpp'),
    }).pipe(
      finalize(() => this.loading.set(false)),
      catchError(error => {
        this.error.set(this.message(error));
        return of({ remote: [], local: [] });
      }),
    ).subscribe(({ remote, local }) => {
      this.remote.set(remote);
      this.local.set(local);
    });
  }

  download(assetId: string): void {
    if (this.mutating()) return;
    this.mutating.set(true);
    this.error.set(null);
    this.progress.set({ operationId: '', phase: 'queued', assetId });
    this.http.post<Envelope<Operation>>('/api/runtimes/llama.cpp/downloads', { assetId }).pipe(
      map(response => response.result),
      catchError(error => {
        this.finishError(this.message(error));
        return of(null);
      }),
    ).subscribe(operation => {
      if (operation) {
        this.events(operation.operationId).subscribe({
          next: event => this.consume(event),
          error: error => this.finishError(this.message(error)),
        });
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
    return this.http.get<Envelope<Array<LlamaRuntime>>>(url).pipe(map(response => response.result));
  }

  private events(operationId: string): Observable<DownloadProgress> {
    return new Observable(observer => {
      const source = new EventSource(`/api/runtimes/llama.cpp/downloads/${encodeURIComponent(operationId)}/events`);
      const receive = (event: MessageEvent<string>) => {
        try {
          const progress = JSON.parse(event.data) as DownloadProgress;
          observer.next(progress);
          if (progress.phase === 'completed' || progress.phase === 'failed') observer.complete();
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
    if (event.phase === 'completed') {
      this.mutating.set(false);
      this.load();
    }
    if (event.phase === 'failed') this.finishError(event.error || 'Runtime download failed');
  }
  private finishError(message: string): void {
    this.error.set(message);
    this.mutating.set(false);
  }
  private message(error: unknown): string {
    if (typeof error === 'object' && error !== null && 'error' in error) {
      const body = error.error;
      if (typeof body === 'object' && body !== null && 'error' in body && typeof body.error === 'string') return body.error;
    }
    return error instanceof Error ? error.message : 'Runtime operation failed';
  }
}
