import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { inject, computed } from '@angular/core';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap, catchError, EMPTY } from 'rxjs';
import { BridgeDto, StarSystemDto, CreateSystemRequest, UpdateSystemRequest } from '../models/models';
import { BridgeApiService } from '../services/bridge-api.service';

interface BridgeState {
  bridges: BridgeDto[];
  activeBridge: BridgeDto | null;
  selectedSystem: StarSystemDto | null;
  isLoading: boolean;
  error: string | null;
}

const initialState: BridgeState = {
  bridges: [],
  activeBridge: null,
  selectedSystem: null,
  isLoading: false,
  error: null
};

export const BridgeStore = signalStore(
  { providedIn: 'root' },

  withState(initialState),

  withComputed(({ activeBridge }) => ({
    // Systèmes triés par ordre pour la visualisation
    orderedSystems: computed(() =>
      [...(activeBridge()?.systems ?? [])].sort((a, b) => a.order - b.order)
    ),
    // Statistiques calculées réactivement
    stats: computed(() => {
      const systems = activeBridge()?.systems ?? [];
      return {
        total:       systems.length,
        done:        systems.filter(s => s.status === 'FINI').length,
        inProgress:  systems.filter(s => s.status === 'CONSTRUCTION').length,
        planned:     systems.filter(s => s.status === 'PLANIFIE').length,
        piles:       systems.filter(s => s.type === 'PILE').length,
        tabliers:    systems.filter(s => s.type === 'TABLIER').length,
        completion:  activeBridge()?.completionPercent ?? 0
      };
    })
  })),

  withMethods((store, api = inject(BridgeApiService)) => ({

    // ── Charger tous les ponts ──────────────────────────────────────────────
    loadBridges: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { isLoading: true, error: null })),
        switchMap(() => api.getBridges().pipe(
          tap((bridges: BridgeDto[]) => patchState(store, { bridges, isLoading: false })),
          catchError((err: Error) => {
            patchState(store, { error: err.message, isLoading: false });
            return EMPTY;
          })
        ))
      )
    ),

    // ── Charger un pont actif ───────────────────────────────────────────────
    loadBridge: rxMethod<number>(
      pipe(
        tap(() => patchState(store, { isLoading: true, error: null })),
        switchMap((id: number) => api.getBridgeById(id).pipe(
          tap((activeBridge: BridgeDto) => patchState(store, { activeBridge, isLoading: false })),
          catchError((err: Error) => {
            patchState(store, { error: err.message, isLoading: false });
            return EMPTY;
          })
        ))
      )
    ),

    // ── Sélectionner un système ────────────────────────────────────────────
    selectSystem(system: StarSystemDto | null): void {
      patchState(store, { selectedSystem: system });
    },

    // ── Ajouter un système ─────────────────────────────────────────────────
    addSystem: rxMethod<CreateSystemRequest>(
      pipe(
        switchMap((request: CreateSystemRequest) => api.addSystem(request).pipe(
          tap(() => {
            const bridgeId = store.activeBridge()?.id;
            if (bridgeId) {
              api.getBridgeById(bridgeId).subscribe(
                (activeBridge: BridgeDto) => patchState(store, { activeBridge })
              );
            }
          }),
          catchError((err: Error) => {
            patchState(store, { error: err.message });
            return EMPTY;
          })
        ))
      )
    ),

    // ── Mettre à jour un système ───────────────────────────────────────────
    updateSystem: rxMethod<{ id: number; request: UpdateSystemRequest }>(
      pipe(
        switchMap(({ id, request }) => api.updateSystem(id, request).pipe(
          tap((updated: StarSystemDto) => {
            const bridge = store.activeBridge();
            if (!bridge) return;
            const systems = bridge.systems.map(s => s.id === updated.id ? updated : s);
            patchState(store, {
              activeBridge: { ...bridge, systems },
              selectedSystem: updated
            });
          }),
          catchError((err: Error) => {
            patchState(store, { error: err.message });
            return EMPTY;
          })
        ))
      )
    ),

    // ── Réordonner un système ──────────────────────────────────────────────
    reorderSystem: rxMethod<{ id: number; previousSystemId: number }>(
      pipe(
        switchMap(({ id, previousSystemId }) => api.moveSystem(id,  previousSystemId ).pipe(
          tap(() => {
            const bridgeId = store.activeBridge()?.id;
            if (bridgeId) {
              api.getBridgeById(bridgeId).subscribe(
                (activeBridge: BridgeDto) => patchState(store, { activeBridge })
              );
            }
          }),
          catchError((err: Error) => {
            patchState(store, { error: err.message });
            return EMPTY;
          })
        ))
      )
    ),

    // ── Supprimer un système ───────────────────────────────────────────────
    deleteSystem: rxMethod<number>(
      pipe(
        switchMap((id: number) => api.deleteSystem(id).pipe(
          tap(() => {
            const bridge = store.activeBridge();
            if (!bridge) return;
            const systems = bridge.systems.filter(s => s.id !== id);
            patchState(store, {
              activeBridge: { ...bridge, systems },
              selectedSystem: null
            });
          }),
          catchError((err: Error) => {
            patchState(store, { error: err.message });
            return EMPTY;
          })
        ))
      )
    )
  }))
);