export interface Source {
  fileName: string;
  chunkIndex: number;
  content: string;
  score: number;
}

export interface AskResponse {
  answer: string;
  sources: Source[];
}

export interface UploadResponse {
  documentId: string;
  fileName: string;
  chunkCount: number;
}

export interface DocumentSummary {
  id: string;
  fileName: string;
  chunkCount: number;
  uploadedUtc: string;
}

export interface AskRequest {
  question: string;
  documentId?: string;
}