import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SystemCapabilityService } from './system-capability.service';

const report = {
  schemaVersion: 1,
  os: { family: 'windows' },
  executableArchitecture: 'amd64',
  cpu: { logicalProcessors: 4 },
  memory: {},
  graphics: [],
  backends: [],
  diagnostics: [],
};

describe('SystemCapabilityService', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [SystemCapabilityService, provideHttpClient(), provideHttpClientTesting()] });
  });
  it('fetches and caches a valid report', () => {
    const service = TestBed.inject(SystemCapabilityService);
    const http = TestBed.inject(HttpTestingController);
    service.load();
    http.expectOne('/api/system').flush(report);
    expect(service.report()?.schemaVersion).toBe(1);
    service.load();
    expect(http.match('/api/system')).toHaveLength(0);
  });
  it('evicts invalid cached data', () => {
    sessionStorage.setItem('coquena.system-capabilities.v1', JSON.stringify({ fetchedAt: Date.now(), report: { schemaVersion: 99 } }));
    const service = TestBed.inject(SystemCapabilityService);
    const http = TestBed.inject(HttpTestingController);
    service.load();
    http.expectOne('/api/system').flush(report);
    expect(service.report()?.os.family).toBe('windows');
  });
});
