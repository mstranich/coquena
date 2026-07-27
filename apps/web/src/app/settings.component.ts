import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { ModelsDirectoryService } from './models-directory.service';
import { RuntimesDirectoryService } from './runtimes-directory.service';

@Component({
  selector: 'app-settings',
  imports: [RouterLink, FormsModule],
  templateUrl: './settings.component.html',
  styleUrl: './control-plane.component.scss',
})
export class SettingsComponent {
  readonly directory = inject(ModelsDirectoryService);
  readonly runtimes = inject(RuntimesDirectoryService);
  readonly input = signal('');
  constructor() {
    this.directory.load();
    this.runtimes.load();
  }
  save(): void {
    this.directory.save(this.input() || this.directory.path());
  }
  saveRuntimes(): void {
    this.runtimes.save(this.runtimeInput() || this.runtimes.path());
  }
  readonly runtimeInput = signal('');
}
