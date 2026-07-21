import { Routes } from '@angular/router';
import { FieldsComponent } from './fields.component';
import { PresetsComponent } from './presets.component';

export const routes: Routes = [
  { path: 'fields', component: FieldsComponent },
  { path: 'presets', component: PresetsComponent },
  { path: '', pathMatch: 'full', redirectTo: 'fields' },
];
