import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { ModelsDirectoryService } from './models-directory.service';

@Component({
  selector: 'app-settings',
  imports: [RouterLink, FormsModule],
  templateUrl: './settings.component.html',
  styleUrl: './control-plane.component.scss',
})
export class SettingsComponent {
  readonly directory = inject(ModelsDirectoryService);
  readonly input = signal('');
  constructor() {
    this.directory.load();
  }
  save(): void {
    this.directory.save(this.input() || this.directory.path());
  }
}
