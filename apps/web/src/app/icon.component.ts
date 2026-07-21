import { Component, input } from '@angular/core';

@Component({
  selector: 'app-icon',
  template: `<svg aria-hidden="true" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
    @if (name() === 'add') { <path d="M12 5v14M5 12h14"/> }
    @if (name() === 'delete') { <path d="M4 7h16M10 11v6M14 11v6M9 7l1-3h4l1 3M6 7l1 13h10l1-13"/> }
    @if (name() === 'edit') { <path d="M12 20h9M16 4l4 4L8 20H4v-4L16 4z"/> }
    @if (name() === 'search') { <circle cx="11" cy="11" r="6"/><path d="m20 20-4-4"/> }
  </svg>`,
  styles: ':host{display:inline-flex;vertical-align:middle;margin-right:.2rem}'
})
export class IconComponent { readonly name = input.required<'add' | 'delete' | 'edit' | 'search'>(); }
