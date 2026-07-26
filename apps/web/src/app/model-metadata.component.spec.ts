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
      result: { source: { repository: 'org/repo', filename: 'one.gguf', resolvedRevision: 'abc' }, gguf: { 'general.name': 'One' } },
    });
    await loading;
    first.detectChanges();
    expect(first.nativeElement.textContent).toContain('general.name');
    expect(first.nativeElement.textContent).toContain('Hide metadata');
    expect(second.nativeElement.textContent).not.toContain('general.name');
  });
});
