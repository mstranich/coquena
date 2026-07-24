import { Routes } from '@angular/router';
import { ControlPlaneComponent } from './control-plane.component';
import { FieldsComponent } from './fields.component';
import { PresetsComponent } from './presets.component';
import { SettingsComponent } from './settings.component';

export const routes: Routes = [
  { path: 'dashboard', component: ControlPlaneComponent, data: { page: 'dashboard' } },
  { path: 'runtimes', component: ControlPlaneComponent, data: { page: 'runtimes' } },
  { path: 'models', component: ControlPlaneComponent, data: { page: 'models' } },
  { path: 'models/presets', component: PresetsComponent },
  { path: 'settings', component: SettingsComponent },
  { path: 'settings/fields', component: FieldsComponent },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
];
