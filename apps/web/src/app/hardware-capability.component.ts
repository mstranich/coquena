import { Component, OnInit, inject } from '@angular/core';
import { SystemCapabilityService } from './system-capability.service';

@Component({ selector: 'app-hardware-capability', templateUrl: './hardware-capability.component.html', styleUrl: './hardware-capability.component.scss' })
export class HardwareCapabilityComponent implements OnInit {
  readonly capabilities = inject(SystemCapabilityService);
  ngOnInit(): void { this.capabilities.load(); }
  memory(bytes?: number): string { return bytes === undefined ? 'unknown' : `${(bytes / (1 << 30)).toFixed(1)} GiB`; }
}
