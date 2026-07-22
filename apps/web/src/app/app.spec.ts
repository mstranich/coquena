import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes)],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render the Coquena identity', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Coquena');
  });

  it('should expose browser-memory workspace state', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.status')?.textContent).toContain(
      'Browser-memory workspace',
    );
  });

  it('renders control-plane navigation', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('a[href="/dashboard"]')?.textContent).toContain('Dashboard');
    expect(compiled.querySelector('a[href="/models"]')?.textContent).toContain('Models');
    expect(compiled.querySelector('a[href="/runtimes"]')?.textContent).toContain('Runtimes');
    expect(compiled.querySelector('a[href="/settings"]')?.textContent).toContain('Settings');
  });
});
