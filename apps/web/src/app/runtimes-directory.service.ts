import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { catchError, finalize, of } from 'rxjs';

interface Envelope<T> {
  schemaVersion: number;
  result: T;
}
interface Directory {
  path: string;
}

@Injectable({ providedIn: 'root' })
export class RuntimesDirectoryService {
  readonly path = signal('');
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly saved = signal(false);
  constructor(private readonly http: HttpClient) {}
  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.http.get<Envelope<Directory>>('/api/config/runtimes-directory').pipe(
      finalize(() => this.loading.set(false)),
      catchError(e => {
        this.error.set(this.message(e));
        return of(null);
      }),
    ).subscribe(v => {
      if (v) this.path.set(v.result.path);
    });
  }
  save(path: string): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.saved.set(false);
    this.error.set(null);
    this.http.put<Envelope<Directory>>('/api/config/runtimes-directory', { path }).pipe(
      finalize(() => this.saving.set(false)),
      catchError(e => {
        this.error.set(this.message(e));
        return of(null);
      }),
    ).subscribe(v => {
      if (v) {
        this.path.set(v.result.path);
        this.saved.set(true);
      }
    });
  }
  private message(error: unknown): string {
    if (typeof error === 'object' && error !== null && 'error' in error) {
      const body = error.error;
      if (typeof body === 'object' && body !== null && 'error' in body && typeof body.error === 'string') return body.error;
    }
    return 'Unable to update runtimes directory';
  }
}
