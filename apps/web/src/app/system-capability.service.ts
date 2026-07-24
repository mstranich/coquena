import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { catchError, finalize, map, Observable, of, shareReplay, tap } from 'rxjs';

export type CapabilityState = 'available' | 'unavailable' | 'unknown' | 'unsupported';
export interface SystemReport {
  schemaVersion: number;
  os: { family: string; name?: string; version?: string; nativeArchitecture?: string; };
  executableArchitecture: string;
  cpu: { vendor?: string; model?: string; logicalProcessors: number; features?: Array<string>; };
  memory: { totalBytes?: number; };
  graphics: Array<{ id: string; name: string; vendor?: string; memoryBytes?: number; type?: string; }>;
  backends: Array<{ type: string; state: CapabilityState; version?: string; diagnostics?: Array<{ message: string; }>; }>;
  diagnostics: Array<{ message: string; }>;
}

type CacheEnvelope = { fetchedAt: number; report: SystemReport; };
const CACHE_KEY = 'coquena.system-capabilities.v1';
const CACHE_TTL_MS = 15 * 60 * 1000;

@Injectable({ providedIn: 'root' })
export class SystemCapabilityService {
  readonly report = signal<SystemReport | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private request$: Observable<SystemReport> | null = null;

  constructor(private readonly http: HttpClient) {}

  load(force = false): void {
    if (!force) {
      const cached = this.readCache();
      if (cached) {
        this.report.set(cached);
        this.error.set(null);
        return;
      }
    }
    if (this.request$) return;
    this.loading.set(true);
    this.error.set(null);
    this.request$ = this.http.get<SystemReport>('/api/system').pipe(
      map(report => {
        if (!this.isValidReport(report)) throw new Error('Unsupported system report');
        return report;
      }),
      tap(report => {
        this.report.set(report);
        this.writeCache(report);
      }),
      catchError((error: unknown) => {
        this.error.set(error instanceof Error ? error.message : 'Hardware information is unavailable');
        return of(null as unknown as SystemReport);
      }),
      finalize(() => {
        this.loading.set(false);
        this.request$ = null;
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    this.request$.subscribe();
  }

  private readCache(): SystemReport | null {
    try {
      const raw = sessionStorage.getItem(CACHE_KEY);
      if (!raw) return null;
      const envelope = JSON.parse(raw) as CacheEnvelope;
      if (!envelope.fetchedAt || Date.now() - envelope.fetchedAt > CACHE_TTL_MS || !this.isValidReport(envelope.report)) {
        sessionStorage.removeItem(CACHE_KEY);
        return null;
      }
      return envelope.report;
    } catch {
      sessionStorage.removeItem(CACHE_KEY);
      return null;
    }
  }

  private writeCache(report: SystemReport): void {
    try {
      sessionStorage.setItem(CACHE_KEY, JSON.stringify({ fetchedAt: Date.now(), report }));
    } catch { /* storage is optional */ }
  }

  private isValidReport(report: SystemReport | null | undefined): report is SystemReport {
    return !!report && report.schemaVersion === 1 && typeof report.os?.family === 'string'
      && typeof report.executableArchitecture === 'string' && report.cpu?.logicalProcessors >= 1
      && Array.isArray(report.graphics) && Array.isArray(report.backends);
  }
}
