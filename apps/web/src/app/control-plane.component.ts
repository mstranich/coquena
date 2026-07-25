import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HardwareCapabilityComponent } from './hardware-capability.component';
import { LlamacppRuntimeService } from './llamacpp-runtime.service';

@Component({
  selector: 'app-control-plane',
  imports: [RouterLink, HardwareCapabilityComponent],
  templateUrl: './control-plane.component.html',
  styleUrl: './control-plane.component.scss',
})
export class ControlPlaneComponent {
  private readonly route = inject(ActivatedRoute);
  readonly runtimes = inject(LlamacppRuntimeService);
  readonly page = this.route.snapshot.data['page'] as 'dashboard' | 'runtimes' | 'models';
  readonly models = [
    { name: 'Qwen3 8B', details: 'Q4_K_M · 5.1 GB', meta: 'Q4_K_M · 5.1 GB · Ready', preset: 'Balanced', state: 'Ready', presets: ['Balanced', 'Fast'] },
    { name: 'Gemma 3 4B', details: 'Q8_0 · 4.8 GB', meta: 'Q8_0 · 4.8 GB · Ready', preset: 'Default', state: 'Loaded', presets: ['Default'] },
    { name: 'Llama 3.2 3B', details: 'Q4_K_M · 2.0 GB', meta: 'Q4_K_M · 2.0 GB · Ready', preset: 'Fast', state: 'Available', presets: ['Fast'] },
  ];

  constructor() {
    if (this.page === 'runtimes') this.runtimes.load();
  }
}
