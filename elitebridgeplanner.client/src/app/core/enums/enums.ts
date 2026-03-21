 import { ColonizationStatus, SystemType } from '@core/models/models';

 export const systemTypeOptions: { value: SystemType; label: string }[] = [
    { value: 'DEBUT', label: 'SYSTEM.TYPE.START' },
    { value: 'PILE', label: 'SYSTEM.TYPE.PIER' },
    { value: 'TABLIER', label: 'SYSTEM.TYPE.DECK' },
    { value: 'FIN', label: 'SYSTEM.TYPE.END' }
  ];

export const systemStatusOptions: { value: ColonizationStatus; label: string }[] = [
    { value: 'PLANIFIE', label: 'SYSTEM.STATUS.PLANNED' },
    { value: 'CONSTRUCTION', label: 'SYSTEM.STATUS.BUILDING' },
    { value: 'FINI', label: 'SYSTEM.STATUS.COMPLETE' }
  ];