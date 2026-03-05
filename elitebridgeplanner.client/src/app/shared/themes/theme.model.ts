export type ThemeId = 'blue' | 'orange' | 'green' | 'red';

export interface Theme {
  id: ThemeId;
  label: string;
  color: string;   // couleur d'aperçu pour le sélecteur
}

export const THEMES: Theme[] = [
  { id: 'blue',   label: 'Cyan',    color: '#00d4ff' },
  { id: 'orange', label: 'Orange',  color: '#ff8c00' },
  { id: 'green',  label: 'Vert',    color: '#00ff88' },
  { id: 'red',    label: 'Rouge',   color: '#ff3366' },
];

export const THEME_STORAGE_KEY = 'elite_bridge_theme';
