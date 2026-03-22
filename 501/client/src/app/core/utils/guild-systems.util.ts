import type { GuildSystemBgsDto } from '../models/guild-systems.model';

const CONFLICT_STATES = ['conflit', 'war', 'civil war', 'civil unrest', 'election', 'retribution'];

/** Vrai si le système a un état de conflit (Conflit, War, Civil War, etc.). */
export function hasConflictState(sys: GuildSystemBgsDto): boolean {
  const parts = sys.states?.length
    ? sys.states
    : (sys.state?.trim() ? sys.state.split(',').map((s) => s.trim()) : []);
  return parts.some((s) => CONFLICT_STATES.includes(s.toLowerCase()));
}
