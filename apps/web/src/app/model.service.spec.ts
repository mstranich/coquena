import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ModelService } from './model.service';

const job = (status: string, downloadedBytes: number, expectedSizeBytes: number) => ({
  id: 'job',
  source: { repository: 'org/repo', filename: 'm.gguf', resolvedRevision: 'abc' },
  targetPath: '/models/org/repo/m.gguf',
  status,
  downloadedBytes,
  expectedSizeBytes,
});

class FakeEventSource {
  static instances: Array<FakeEventSource> = [];
  listeners = new Map<string, EventListener>();
  onerror: ((event: Event) => void) | null = null;
  closed = false;
  constructor(readonly url: string) {
    FakeEventSource.instances.push(this);
  }
  addEventListener(type: string, listener: EventListener) {
    this.listeners.set(type, listener);
  }
  close() {
    this.closed = true;
  }
  emit(type: string, data: object) {
    this.listeners.get(type)?.({ data: JSON.stringify(data) } as MessageEvent);
  }
}
describe('ModelService', () => {
  beforeEach(() => {
    FakeEventSource.instances = [];
    vi.stubGlobal('EventSource', FakeEventSource);
    TestBed.configureTestingModule({ providers: [ModelService, provideHttpClient(), provideHttpClientTesting()] });
  });
  afterEach(() => vi.unstubAllGlobals());
  it('resolves candidates and downloads the selected filename', async () => {
    const service = TestBed.inject(ModelService);
    const http = TestBed.inject(HttpTestingController);
    const resolving = service.resolve('org/repo');
    http.expectOne('/api/models/resolve').flush({
      schemaVersion: 1,
      result: [{ source: { repository: 'org/repo', filename: 'm.gguf' }, artifact: { sizeBytes: 24 } }],
    });
    await resolving;
    expect(service.candidates()[0].source.filename).toBe('m.gguf');
    const downloading = service.download('org/repo', 'm.gguf');
    const request = http.expectOne('/api/models/downloads');
    expect(request.request.body).toEqual({ reference: 'org/repo', selector: 'm.gguf' });
    request.flush({ schemaVersion: 1, result: job('pending', 0, 24) });
    await downloading;
    expect(service.library()[0].job?.status).toBe('pending');
    FakeEventSource.instances[0].emit('completed', job('completed', 24, 24));
    expect(service.library()[0].job?.status).toBe('completed');
    expect(FakeEventSource.instances[0].closed).toBe(true);
    http.expectOne('/api/models/library').flush({ schemaVersion: 1, result: [] });
  });
  it('shows HTTP errors', async () => {
    const service = TestBed.inject(ModelService);
    const operation = service.resolve('bad');
    TestBed.inject(HttpTestingController).expectOne('/api/models/resolve').flush({}, { status: 400, statusText: 'Bad request' });
    await operation;
    expect(service.error()).toBe('Unable to resolve GGUF artifacts.');
  });

  it('keeps inspected GGUF metadata as a read-only response', async () => {
    const service = TestBed.inject(ModelService);
    const inspection = service.inspectRemote('org/repo', 'm.gguf');
    const request = TestBed.inject(HttpTestingController).expectOne('/api/models/inspect');
    expect(request.request.body).toEqual({ reference: 'org/repo', selector: 'm.gguf' });
    request.flush({
      schemaVersion: 1,
      result: { source: { repository: 'org/repo', filename: 'm.gguf', resolvedRevision: 'abc' }, gguf: { 'general.name': 'Example' } },
    });
    await expect(inspection).resolves.toMatchObject({ gguf: { 'general.name': 'Example' } });
  });

  it('loads installed metadata lazily from its dedicated endpoint', async () => {
    const service = TestBed.inject(ModelService);
    const metadata = service.installedMetadata('org/repo', 'm.gguf');
    const request = TestBed.inject(HttpTestingController).expectOne('/api/models/metadata');
    expect(request.request.body).toEqual({ repository: 'org/repo', filename: 'm.gguf' });
    request.flush({
      schemaVersion: 1,
      result: { source: { repository: 'org/repo', filename: 'm.gguf', resolvedRevision: 'abc' }, gguf: { 'general.name': 'Installed' } },
    });
    await expect(metadata).resolves.toMatchObject({ gguf: { 'general.name': 'Installed' } });
  });

  it('closes a paused stream and reconnects when resumed', async () => {
    const service = TestBed.inject(ModelService);
    const http = TestBed.inject(HttpTestingController);
    const downloading = service.download('org/repo', 'm.gguf');
    http.expectOne('/api/models/downloads').flush({ schemaVersion: 1, result: job('pending', 0, 100) });
    await downloading;
    FakeEventSource.instances[0].emit('paused', job('paused', 50, 100));
    expect(FakeEventSource.instances[0].closed).toBe(true);
    http.expectOne('/api/models/library').flush({
      schemaVersion: 1,
      result: [{ source: job('paused', 50, 100).source, targetPath: job('paused', 50, 100).targetPath, state: 'paused', job: job('paused', 50, 100) }],
    });
    const resuming = service.action(job('paused', 50, 100), 'resume');
    http.expectOne('/api/models/downloads/job/resume').flush({
      schemaVersion: 1,
      result: job('downloading', 50, 100),
    });
    await resuming;
    expect(FakeEventSource.instances).toHaveLength(2);
    expect(FakeEventSource.instances[1].url).toContain('/job/events');
  });

  it('shows the backend diagnostic when a download action fails', async () => {
    const service = TestBed.inject(ModelService);
    const http = TestBed.inject(HttpTestingController);
    const action = service.action(job('paused', 10, 100), 'resume');
    http.expectOne('/api/models/downloads/job/resume').flush(
      { error: 'partial file is smaller than its persisted checkpoint' },
      { status: 409, statusText: 'Conflict' },
    );
    await action;
    expect(service.error()).toContain('persisted checkpoint');
  });

  it('tracks multiple downloads independently in the model library', async () => {
    const service = TestBed.inject(ModelService);
    const http = TestBed.inject(HttpTestingController);
    const first = service.download('org/one', 'one.gguf');
    http.expectOne('/api/models/downloads').flush({ schemaVersion: 1, result: job('pending', 0, 100) });
    await first;
    const secondJob = {
      ...job('pending', 0, 200),
      id: 'job-2',
      source: { repository: 'org/two', filename: 'two.gguf', resolvedRevision: 'def' },
      targetPath: '/models/org/two/two.gguf',
    };
    const second = service.download('org/two', 'two.gguf');
    http.expectOne('/api/models/downloads').flush({ schemaVersion: 1, result: secondJob });
    await second;

    FakeEventSource.instances[0].emit('progress', job('downloading', 40, 100));
    FakeEventSource.instances[1].emit('progress', { ...secondJob, status: 'downloading', downloadedBytes: 80 });

    expect(service.library().map(entry => entry.job?.downloadedBytes)).toEqual([40, 80]);
  });
});
