import type { Game, NativeState, View } from "./types";
export type ViewSnapshot = {
  view: View;
  lastCatalogView: View;
  query: string;
  onlySequence: boolean;
  hideQueued: boolean;
  selecting: boolean;
  selection: Game[];
  minutes: string;
  preferenceMinutes: number;
};
declare global {
  interface Window {
    __MIRAGE_VIEW_STATE__?: ViewSnapshot | null;
    __MIRAGE_CAPTURE_VIEW__?: () => ViewSnapshot;
    chrome?: {
      webview?: {
        postMessage: (message: unknown) => void;
        addEventListener: (
          type: string,
          callback: (event: MessageEvent) => void,
        ) => void;
      };
    };
    __MIRAGE_READY__?: boolean;
    __MIRAGE_SMOKE__?: (scene?: string) => Promise<unknown>;
    __MIRAGE_RUNTIME_SMOKE__?: () => Promise<unknown>;
    __MIRAGE_RUNTIME_REPORT__?: unknown;
    __MIRAGE_REPORT__?: unknown;
  }
}
const pending = new Map<
  string,
  {
    resolve: (value: any) => void;
    reject: (error: Error) => void;
    timer: ReturnType<typeof setTimeout> | undefined;
  }
>();
const listeners = new Set<(state: NativeState) => void>();
window.chrome?.webview?.addEventListener("message", (event) => {
  const message = event.data;
  if (message?.type === "state") {
    listeners.forEach((listener) => listener(message.state));
    return;
  }
  const request = pending.get(message?.id);
  if (!request) return;
  clearTimeout(request.timer);
  pending.delete(message.id);
  if (message.error) request.reject(new Error(message.error));
  else {
    const state = message.result?.state ?? message.result;
    if (
      typeof state?.version === "string" &&
      typeof state?.catalogCount === "number" &&
      state?.preferences &&
      Array.isArray(state?.playlist)
    ) listeners.forEach((listener) => listener(state));
    request.resolve(message.result);
  }
});
export function request<T = NativeState>(
  command: string,
  payload: Record<string, unknown> = {},
): Promise<T> {
  if (!window.chrome?.webview)
    return Promise.reject(
      new Error("Abra esta interface pelo aplicativo Mirage."),
    );
  return new Promise<T>((resolve, reject) => {
    const id = crypto.randomUUID();
    // A native folder picker can remain open for as long as the user needs.
    const timer = command === "saves.chooseFolder" || command === "saves.scanInstalled" || command === "saves.validate" || command === "saves.export" || command.startsWith("saves.restore.") ? undefined : setTimeout(
      () => {
        pending.delete(id);
        reject(
          new Error("A operação demorou mais que o esperado. Tente novamente."),
        );
      },
      command === "refresh" ? 90000 : 20000,
    );
    pending.set(id, { resolve, reject, timer });
    try { window.chrome!.webview!.postMessage({ id, command, payload }); }
    catch { clearTimeout(timer); pending.delete(id); reject(new Error("Não foi possível comunicar com o Mirage. Reabra a janela.")); }
  });
}
export function subscribe(listener: (state: NativeState) => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
