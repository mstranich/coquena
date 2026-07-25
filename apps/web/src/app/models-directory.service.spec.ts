import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ModelsDirectoryService } from './models-directory.service';

describe('ModelsDirectoryService', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [ModelsDirectoryService, provideHttpClient(), provideHttpClientTesting()] }));
  it('loads and saves the configured directory', () => {
    const service = TestBed.inject(ModelsDirectoryService);
    const http = TestBed.inject(HttpTestingController);
    service.load();
    http.expectOne('/api/config/models-directory').flush({ schemaVersion: 1, result: { path: 'C:/Models' } });
    expect(service.path()).toBe('C:/Models');
    service.save('D:/Models');
    http.expectOne('/api/config/models-directory').flush({ schemaVersion: 1, result: { path: 'D:/Models' } });
    expect(service.path()).toBe('D:/Models');
    expect(service.saved()).toBe(true);
  });
  it('retains the last successful path on save failure', () => {
    const service = TestBed.inject(ModelsDirectoryService);
    const http = TestBed.inject(HttpTestingController);
    service.load();
    http.expectOne('/api/config/models-directory').flush({ schemaVersion: 1, result: { path: 'C:/Models' } });
    service.save('missing');
    http.expectOne('/api/config/models-directory').flush({ error: 'models directory is unavailable' }, { status: 400, statusText: 'Bad Request' });
    expect(service.path()).toBe('C:/Models');
    expect(service.error()).toBe('models directory is unavailable');
  });
});
