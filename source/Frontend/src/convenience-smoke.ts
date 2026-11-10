import { request } from "./bridge";
import type { NativeState } from "./types";
const pause = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
async function until(test: () => boolean, description: string) {
  for (let i = 0; i < 100; i++) { if (test()) return; await pause(50); }
  throw new Error(description);
}
async function edit(input: HTMLInputElement, value: string) {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
  input.dispatchEvent(new Event("input", { bubbles: true }));
  await pause(60);
}
async function state() { return (await request<{state: NativeState}>("boot")).state; }
export async function runConvenienceSmoke() {
  const checks: string[] = [];
  const check = (value: unknown, name: string) => { if (!value) throw new Error(name); checks.push(name); };
  const original = (await state()).preferences;
  document.dispatchEvent(new KeyboardEvent("keydown", { key: "P", ctrlKey: true, shiftKey: true, bubbles: true, cancelable: true }));
  await until(() => Boolean(document.querySelector('[data-testid="quick-actions"]')), "command palette did not open");
  const search = document.querySelector<HTMLInputElement>('[aria-label="Buscar ação"]')!;
  await until(() => document.activeElement === search, "command search did not get focus");
  check(true, "Ctrl Shift P opens the command palette and focuses its search");
  search.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true, cancelable: true }));
  await pause(60);
  const active = document.getElementById(search.getAttribute("aria-activedescendant")!);
  check(active?.getAttribute("data-action-id") === "library", "arrow keys navigate enabled commands through aria-activedescendant");
  await edit(search, "CONFIGURACOES");
  check(document.querySelectorAll('.quick-results [role="option"]').length === 1, "command search matches uppercase text without accents");
  search.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true, cancelable: true }));
  await until(() => Boolean(document.querySelector('[data-testid="preferences-panel"]')), "settings did not open from the palette");
  await pause(400);
  const duration = document.querySelector<HTMLInputElement>('[aria-label="Duração padrão"]')!;
  check(document.activeElement === duration, "switching dialogs keeps focus in the new settings dialog");
  await edit(duration, "0"); duration.blur();
  check(document.querySelector('.preference-error') !== null && (await state()).preferences.minutes === original.minutes, "invalid default duration stays visible without changing native preferences");
  duration.focus(); await edit(duration, "31"); await pause(600);
  check((await state()).preferences.minutes === 31, "valid default duration is saved automatically after typing stops");
  duration.focus(); await edit(duration, "23"); duration.blur();
  const notifications = document.querySelector<HTMLButtonElement>('[role="switch"][aria-labelledby="preference-notifications"]')!;
  notifications.click();
  await until(() => notifications.getAttribute("aria-checked") === String(!original.notifications), "first notification click after editing duration was lost");
  const changed = await state();
  check(changed.preferences.minutes === 23 && changed.preferences.notifications === !original.notifications, "first switch click after a duration edit saves both settings");
  const tray = document.querySelector<HTMLButtonElement>('[role="switch"][aria-labelledby="preference-minimizeToTray"]')!;
  tray.click(); await until(() => tray.getAttribute("aria-checked") === String(!original.minimizeToTray), "tray preference did not change");
  check((await state()).preferences.minimizeToTray === !original.minimizeToTray, "tray switch updates native minimize behavior");
  check(document.querySelector('.preference-economy')?.textContent?.includes("15 segundos"), "settings explain the existing automatic memory economy");
  duration.focus(); await edit(duration, "19");
  duration.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true }));
  await until(() => !document.querySelector('[data-testid="preferences-panel"]'), "settings did not close");
  await pause(80);
  check((await state()).preferences.minutes === 19, "closing settings immediately still commits a valid duration draft");
  document.dispatchEvent(new KeyboardEvent("keydown", { key: ",", ctrlKey: true, bubbles: true, cancelable: true }));
  await until(() => Boolean(document.querySelector('[data-testid="preferences-panel"]')), "Ctrl comma did not open settings");
  check(document.querySelector<HTMLButtonElement>('[role="switch"][aria-labelledby="preference-notifications"]')?.getAttribute("aria-checked") === String(!original.notifications), "Ctrl comma reopens settings with current preferences");
  const reopenedDuration = document.querySelector<HTMLInputElement>('[aria-label="Duração padrão"]')!;
  await edit(reopenedDuration, "27");
  window.dispatchEvent(new Event("mirage.before-background"));
  await pause(80);
  check((await state()).preferences.minutes === 27, "background transition commits pending settings before suspending the page");
  document.querySelector<HTMLButtonElement>('[role="dialog"][aria-modal="true"] [aria-label="Fechar diálogo"]')!.click();
  await until(() => !document.querySelector('[data-testid="preferences-panel"]'), "settings did not close again");
  await request("configure", { minutes: 16, showWindow: original.showWindow, notifications: original.notifications, minimizeToTray: original.minimizeToTray });
  document.querySelector<HTMLButtonElement>('[data-testid="open-commands"]')!.click();
  await until(() => Boolean(document.querySelector('[data-testid="quick-actions"]')), "palette did not open from sidebar");
  await edit(document.querySelector<HTMLInputElement>('[aria-label="Buscar ação"]')!, "sequencia");
  document.querySelector<HTMLButtonElement>('[data-action-id="queue"]')!.click();
  await until(() => document.querySelector('[data-nav="playlist"]')?.getAttribute("aria-current") === "page", "queue command did not navigate");
  check(true, "sidebar command palette navigates to the playlist");
  await until(() => !document.querySelector('[data-testid="quick-actions"]'), "palette did not finish closing");
  document.dispatchEvent(new KeyboardEvent("keydown", { key: "P", ctrlKey: true, shiftKey: true, bubbles: true, cancelable: true }));
  await until(() => Boolean(document.querySelector('[data-action-id="search"]')), "search command missing");
  document.querySelector<HTMLButtonElement>('[data-action-id="search"]')!.click();
  await until(() => document.activeElement?.classList.contains("search-input") === true, "palette search action did not focus the library search");
  check(true, "search command closes the palette and restores search focus");
  return checks;
}
