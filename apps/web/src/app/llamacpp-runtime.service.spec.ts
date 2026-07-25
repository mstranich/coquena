import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { LlamacppRuntimeService } from './llamacpp-runtime.service';

class FakeEventSource {
  static instances: Array<FakeEventSource> = [];
  readonly listeners = new Map<string, EventListener>();
  onerror: ((event: Event) => void) | null = null;
  closed = false;
  constructor(readonly url: string) {
    FakeEventSource.instances.push(this);
  }
  addEventListener(type: string, listener: EventListener): void {
    this.listeners.set(type, listener);
  }
  close(): void {
    this.closed = true;
  }
  emit(type: string, data: object): void {
    this.listeners.get(type)?.({ data: JSON.stringify(data) } as MessageEvent);
  }
  fail(): void {
    this.onerror?.(new Event('error'));
  }
}

const runtime = { release: 'b1', target: 'cpu', backend: 'cpu', assetId: 'llama-b1-bin-win-cpu-x64.zip', compatibility: 'compatible' };

describe('LlamacppRuntimeService', () => {
  beforeEach(() => {
    FakeEventSource.instances = [];
    vi.stubGlobal('EventSource', FakeEventSource);
    TestBed.configureTestingModule({ providers: [LlamacppRuntimeService, provideHttpClient(), provideHttpClientTesting()] });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('loads compatible and local runtimes', () => {
    const service = TestBed.inject(LlamacppRuntimeService);
    const http = TestBed.inject(HttpTestingController);
    service.load();
    http.expectOne('/api/runtimes/llama.cpp/remote').flush({ schemaVersion: 1, result: [runtime] });
    http.expectOne('/api/runtimes/llama.cpp').flush({ schemaVersion: 1, result: [runtime] });
    expect(service.remote()).toEqual([runtime]);
    expect(service.local()).toEqual([runtime]);
  });

  it('creates an exact download operation, consumes SSE, and refreshes', () => {
    const service = TestBed.inject(LlamacppRuntimeService);
    const http = TestBed.inject(HttpTestingController);
    service.download(runtime.assetId);
    http.expectOne('/api/runtimes/llama.cpp/downloads').flush({ schemaVersion: 1, result: { operationId: 'operation-1' } });
    const events = FakeEventSource.instances[0];
    expect(events.url).toContain('operation-1/events');
    events.emit('progress', { operationId: 'operation-1', phase: 'downloading', totalTransferredBytes: 5, totalBytes: 10 });
    expect(service.progress()?.totalTransferredBytes).toBe(5);
    events.emit('completed', { operationId: 'operation-1', phase: 'completed' });
    expect(events.closed).toBe(true);
    http.expectOne('/api/runtimes/llama.cpp/remote').flush({ schemaVersion: 1, result: [runtime] });
    http.expectOne('/api/runtimes/llama.cpp').flush({ schemaVersion: 1, result: [runtime] });
    expect(service.mutating()).toBe(false);
  });

  it('requires confirmation and removes by exact encoded ID', () => {
    const service = TestBed.inject(LlamacppRuntimeService);
    const http = TestBed.inject(HttpTestingController);
    vi.stubGlobal('confirm', vi.fn(() => false));
    service.remove(runtime.assetId);
    http.expectNone(`/api/runtimes/llama.cpp/${runtime.assetId}`);
    vi.stubGlobal('confirm', vi.fn(() => true));
    service.remove(runtime.assetId);
    http.expectOne(`/api/runtimes/llama.cpp/${runtime.assetId}`).flush({ schemaVersion: 1, result: { changed: true } });
    http.expectOne('/api/runtimes/llama.cpp/remote').flush({ schemaVersion: 1, result: [] });
    http.expectOne('/api/runtimes/llama.cpp').flush({ schemaVersion: 1, result: [] });
    expect(service.local()).toEqual([]);
  });

  it('prevents concurrent mutations and exposes stream errors', () => {
    const service = TestBed.inject(LlamacppRuntimeService);
    const http = TestBed.inject(HttpTestingController);
    service.download(runtime.assetId);
    service.download('llama-other-bin-win-cpu-x64.zip');
    const operation = http.expectOne('/api/runtimes/llama.cpp/downloads');
    expect(operation.request.body).toEqual({ assetId: runtime.assetId });
    operation.flush({ schemaVersion: 1, result: { operationId: 'operation-2' } });
    FakeEventSource.instances[0].fail();
    expect(FakeEventSource.instances[0].closed).toBe(true);
    expect(service.error()).toBe('Runtime progress stream disconnected');
    expect(service.mutating()).toBe(false);
  });

  it('handles a failed terminal event without refreshing inventories', () => {
    const service = TestBed.inject(LlamacppRuntimeService);
    const http = TestBed.inject(HttpTestingController);
    service.download(runtime.assetId);
    http.expectOne('/api/runtimes/llama.cpp/downloads').flush({ schemaVersion: 1, result: { operationId: 'operation-3' } });
    FakeEventSource.instances[0].emit('failed', { operationId: 'operation-3', phase: 'failed', error: 'Runtime operation failed' });
    http.expectNone('/api/runtimes/llama.cpp/remote');
    expect(service.error()).toBe('Runtime operation failed');
    expect(service.mutating()).toBe(false);
  });

  it('shows the safe API error returned by the backend', () => {
    const service = TestBed.inject(LlamacppRuntimeService);
    const http = TestBed.inject(HttpTestingController);
    service.download(runtime.assetId);
    http.expectOne('/api/runtimes/llama.cpp/downloads').flush(
      { error: 'Coquena is not initialized; run coquena init' },
      { status: 409, statusText: 'Conflict' },
    );
    expect(service.error()).toBe('Coquena is not initialized; run coquena init');
    expect(service.mutating()).toBe(false);
  });
});
