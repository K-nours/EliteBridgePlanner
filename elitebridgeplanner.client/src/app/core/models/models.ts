// Miroirs exacts des DTOs .NET — à maintenir en sync avec Dtos.cs

export type SystemType = 'DEBUT' | 'PILE' | 'TABLIER' | 'FIN';
export type ColonizationStatus = 'PLANIFIE' | 'CONSTRUCTION' | 'FINI';

export interface StarSystemDto {
  id: number;
  name: string;
  type: SystemType;
  status: ColonizationStatus;
  order: number;
  architectId: string | null;
  architectName: string | null;
  bridgeId: number;
  updatedAt: string;
}

export interface BridgeDto {
  id: number;
  name: string;
  description: string | null;
  createdByName: string | null;
  systems: StarSystemDto[];
  completionPercent: number;
  createdAt: string;
}

export interface CreateBridgeRequest {
  name: string;
  description?: string;
}

export interface CreateSystemRequest {
  name: string;
  type: SystemType;
  status: ColonizationStatus;
  insertAfterOrder: number;
  architectId: string | null;
  bridgeId: number;
}

export interface UpdateSystemRequest {
  name?: string;
  type?: SystemType;
  status?: ColonizationStatus;
  architectId?: string;
}

export interface ReorderSystemRequest {
  newOrder: number;
}

// Auth
export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  commanderName: string;
  password: string;
}

export interface AuthResponse {
  token: string;
  commanderName: string;
  email: string;
  expiresAt: string;
}

// Session courante stockée après login
export interface CurrentUser {
  token: string;
  commanderName: string;
  email: string;
  expiresAt: Date;
}
