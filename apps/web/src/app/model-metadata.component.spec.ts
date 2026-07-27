import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ModelMetadataComponent } from './model-metadata.component';

describe('ModelMetadataComponent', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [ModelMetadataComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    })
  );

  it('keeps inspection state independent for each quantization', async () => {
    const first = TestBed.createComponent(ModelMetadataComponent);
    first.componentRef.setInput('repository', 'org/repo');
    first.componentRef.setInput('filename', 'one.gguf');
    first.componentRef.setInput('reference', 'org/repo');
    first.detectChanges();
    const second = TestBed.createComponent(ModelMetadataComponent);
    second.componentRef.setInput('repository', 'org/repo');
    second.componentRef.setInput('filename', 'two.gguf');
    second.componentRef.setInput('reference', 'org/repo');
    second.detectChanges();

    const loading = first.componentInstance.toggle();
    first.detectChanges();
    second.detectChanges();
    expect(first.nativeElement.textContent).toContain('Loading');
    expect(second.nativeElement.textContent).toContain('Inspect');

    TestBed.inject(HttpTestingController).expectOne('/api/models/inspect').flush({
      result: {
        source: { repository: 'org/repo', filename: 'one.gguf', resolvedRevision: 'abc' },
        classification: { type: 'model', architecture: 'gemma4' },
        metadata: { 'general.name': 'One' },
      },
    });
    await loading;
    first.detectChanges();
    expect(first.nativeElement.textContent).toContain('general.name');
    expect(first.nativeElement.textContent).toContain('Hide metadata');
    expect(second.nativeElement.textContent).not.toContain('general.name');
  });

  it('shows the declared type in the candidate table', async () => {
    const fixture = TestBed.createComponent(ModelMetadataComponent);
    fixture.componentRef.setInput('repository', 'org/repo');
    fixture.componentRef.setInput('filename', 'one.gguf');
    fixture.componentRef.setInput('reference', 'org/repo');
    fixture.detectChanges();

    const loading = fixture.componentInstance.toggle();
    TestBed.inject(HttpTestingController).expectOne('/api/models/inspect').flush({
      result: {
        source: { repository: 'org/repo', filename: 'one.gguf', resolvedRevision: 'abc' },
        classification: { type: 'mmproj', architecture: 'clip', projectionDim: 5120 },
        metadata: { 'general.name': 'One' },
      },
    });
    await loading;
    fixture.detectChanges();

    const rows = fixture.componentInstance.rows();
    expect(rows[0]).toEqual({ key: 'classification.type', value: 'mmproj' });
    expect(fixture.nativeElement.textContent).toContain('mmproj');
    expect(fixture.nativeElement.textContent).toContain('5120');
    expect(fixture.nativeElement.querySelectorAll('th')[0].textContent).toContain('Metadata');
  });

  it('lazily loads an installed entry classification from the metadata endpoint', async () => {
    const fixture = TestBed.createComponent(ModelMetadataComponent);
    fixture.componentRef.setInput('repository', 'org/repo');
    fixture.componentRef.setInput('filename', 'one.gguf');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Show metadata');

    const loading = fixture.componentInstance.toggle();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/models/metadata');
    expect(request.request.body).toEqual({ repository: 'org/repo', filename: 'one.gguf' });
    request.flush({
      result: {
        source: { repository: 'org/repo', filename: 'one.gguf', resolvedRevision: 'abc' },
        classification: { type: 'model', architecture: 'gemma4' },
        metadata: { 'general.name': 'Installed' },
      },
    });
    await loading;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('classification.type');
    expect(fixture.nativeElement.textContent).toContain('gemma4');
  });
});
