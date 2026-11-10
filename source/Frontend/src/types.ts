export type Game = {
  id: string;
  name: string;
  initial: string;
  iconUrl: string | null;
  heroUrl: string | null;
  supportsPlaylist: boolean;
  executables: string[];
};
export type QueueEntry = {
  entryId: string;
  gameId: string;
  gameName: string;
  executable: string;
  durationMinutes: number;
  showWindow: boolean;
  iconUrl: string | null;
  position: number;
  status: "queued" | "done" | "playing" | "next";
};
export type Session = {
  game: Game;
  confirmed: boolean;
  elapsedSeconds: number;
  isPlaylist: boolean;
  index: number;
  count: number;
  remainingSeconds: number;
  durationMinutes: number;
  next: { name: string; minutes: number } | null;
  itemProgress: number;
};
export type SavedPlaylist = {
  id: string;
  name: string;
  count: number;
  totalMinutes: number;
  games: { name: string; iconUrl: string | null }[];
};
export type NativeState = {
  version: string;
  preview: boolean;
  catalogCount: number;
  busy: boolean;
  storage: { warning: string | null; replacementRequired: boolean };
  selected: Game | null;
  variantIndex: number;
  preferences: {
    minutes: number;
    showWindow: boolean;
    minimizeToTray: boolean;
    notifications: boolean;
  };
  favorites: string[];
  recentCount: number;
  playlist: QueueEntry[];
  savedPlaylists: SavedPlaylist[];
  canUndoPlaylist: boolean;
  undoLabel: string | null;
  playlistTiming: { totalMinutes: number; remainingSeconds: number };
  session: Session | null;
  status: { title: string; description: string } | null;
};
export type SearchResult = { items: Game[]; total: number; hasMore: boolean };
export type View = "explore" | "favorites" | "recent" | "playlist" | "saves";
export type SortOrder = "default" | "nameAsc" | "nameDesc";
