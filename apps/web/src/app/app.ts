import { Component, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterOutlet } from '@angular/router';
import { of } from 'rxjs';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('Coquena');

  private readonly workspaceStatus$ = of('Development workspace ready');
  protected readonly workspaceStatus = toSignal(this.workspaceStatus$, {
    requireSync: true,
  });
}
