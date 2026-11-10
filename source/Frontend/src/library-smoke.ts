import { request } from "./bridge";
import type { NativeState, SearchResult } from "./types";

const pause = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
async function until(condition: () => boolean, message: string) {
  for (let i = 0; i < 120; i++) {
    if (condition()) return;
    await pause(50);
  }
  throw new Error(message);
}
async function edit(selector: string, value: string) {
  const input = document.querySelector<HTMLInputElement>(selector)!;
  Object.getOwnPropertyDescriptor(
    HTMLInputElement.prototype,
    "value",
  )!.set!.call(input, value);
  input.dispatchEvent(new Event("input", { bubbles: true }));
  await pause(90);
}
const state = async () => (await request<{ state: NativeState }>("boot")).state;
async function openDisplay() {
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Opções de exibição da biblioteca"]',
    )!
    .click();
  await until(
    () => !!document.querySelector("#library-sort"),
    "display options did not open",
  );
}
export async function resetLibraryDisplay() {
  await openDisplay();
  const reset = document.querySelector<HTMLButtonElement>(".display-reset")!;
  if (!reset.disabled) reset.click();
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Fechar opções de exibição"]',
    )!
    .click();
  await until(
    () => !document.querySelector(".library-options"),
    "display options did not close",
  );
  await pause(200);
}
export async function runLibrarySmoke() {
  const checks: string[] = [];
  const check = (value: unknown, label: string) => {
    if (!value) throw new Error(label);
    checks.push(label);
  };
  const original = await state();
  const expected = await request<SearchResult>("search", { sort: "nameAsc" });
  await openDisplay();
  const sort = document.querySelector<HTMLSelectElement>("#library-sort")!;
  sort.value = "nameAsc";
  sort.dispatchEvent(new Event("change", { bubbles: true }));
  await until(
    () =>
      document.querySelector<HTMLElement>(".game-card")?.dataset.gameId ===
      expected.items[0].id,
    "alphabetical display did not match the native search",
  );
  check(
    localStorage.getItem("mirage.librarySort") === "nameAsc",
    "library remembers the chosen name order locally",
  );
  sort.dispatchEvent(
    new KeyboardEvent("keydown", {
      key: "Escape",
      bubbles: true,
      cancelable: true,
    }),
  );
  await until(
    () => !document.querySelector(".library-options"),
    "Escape did not close display options",
  );
  check(
    document.activeElement?.getAttribute("aria-label") ===
      "Opções de exibição da biblioteca",
    "closing display options returns focus to its trigger",
  );
  const cyber = (
    await request<SearchResult>("search", { query: "Cyberpunk 2077" })
  ).items.find((game) => game.name === "Cyberpunk 2077")!;
  const elden = (
    await request<SearchResult>("search", { query: "ELDEN RING" })
  ).items.find((game) => game.name === "ELDEN RING")!;
  await edit(".search-input", cyber.name);
  await until(
    () => !!document.querySelector(`[data-game-id="${cyber.id}"]`),
    "Cyberpunk not visible for library checks",
  );
  const originalCount = original.playlist.filter(
    (item) => item.gameId === cyber.id,
  ).length;
  check(
    document
      .querySelector(`[data-game-id="${cyber.id}"] .card-queue-badge`)
      ?.textContent?.trim() === String(originalCount),
    "game cards show the actual number of queued entries",
  );
  await openDisplay();
  const filters = document.querySelectorAll<HTMLInputElement>(
    ".display-filter-group input",
  );
  filters[1].click();
  await until(
    () => !document.querySelector(`[data-game-id="${cyber.id}"]`),
    "queued game was not hidden",
  );
  check(
    !!document.querySelector('[aria-label="Mostrar jogos já na lista"]'),
    "active queue filter stays visible as a removable chip",
  );
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Fechar opções de exibição"]',
    )!
    .click();
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Mostrar jogos já na lista"]',
    )!
    .click();
  await until(
    () => !!document.querySelector(`[data-game-id="${cyber.id}"]`),
    "removing queue filter did not restore the title",
  );
  check(true, "removing the active filter immediately restores queued games");
  const before = await state();
  const unsupported = (
    await request<SearchResult>("search", { limit: 120 })
  ).items.find((game) => !game.supportsPlaylist);
  const invalid: Record<string, unknown>[] = [
    { gameIds: [], minutes: 16 },
    { gameIds: [cyber.id, cyber.id], minutes: 16 },
    { gameIds: [cyber.id, "999999999999999999"], minutes: 16 },
    { gameIds: [cyber.id, null], minutes: 16 },
    { gameIds: [cyber.id], minutes: 0 },
    { gameIds: [cyber.id], minutes: 1.5 },
    { gameIds: [cyber.id], minutes: 1441 },
    {
      gameIds: Array.from({ length: 101 }, (_, i) =>
        String(100000000000000000n + BigInt(i)),
      ),
      minutes: 16,
    },
    { gameIds: "invalid", minutes: 16 },
  ];
  if (unsupported)
    invalid.push({ gameIds: [cyber.id, unsupported.id], minutes: 16 });
  for (const payload of invalid) {
    let rejected = false;
    try {
      await request("addMany", payload);
    } catch {
      rejected = true;
    }
    check(
      rejected,
      "native boundary rejects invalid bulk selection or duration",
    );
  }
  const afterInvalid = await state();
  check(
    JSON.stringify(before.playlist) === JSON.stringify(afterInvalid.playlist) &&
      before.undoLabel === afterInvalid.undoLabel,
    "rejected bulk requests leave both the queue and undo history untouched",
  );
  await edit(".search-input", elden.name);
  await until(
    () => !!document.querySelector(`[data-game-id="${elden.id}"]`),
    "Elden Ring not visible for multi-selection",
  );
  document
    .querySelector<HTMLButtonElement>('[aria-label="Selecionar vários jogos"]')!
    .click();
  await until(
    () => !!document.querySelector(".card-batch-toggle"),
    "selection checkboxes did not render",
  );
  check(
    !!document.querySelector('[aria-label="Resumo da seleção"]') &&
      !document.querySelector('[data-testid="inspector-add"]'),
    "multi-selection replaces individual game controls with a selection summary",
  );
  document
    .querySelector<HTMLButtonElement>(
      `[data-game-id="${elden.id}"] .card-batch-toggle`,
    )!
    .click();
  await until(
    () => document.querySelector(".batch-count strong")?.textContent === "1",
    "first game did not enter selection",
  );
  await edit(".search-input", cyber.name);
  await until(
    () => !!document.querySelector(`[data-game-id="${cyber.id}"]`),
    "second query did not render",
  );
  document
    .querySelector<HTMLButtonElement>(
      `[data-game-id="${cyber.id}"] .card-batch-toggle`,
    )!
    .click();
  await until(
    () => document.querySelector(".batch-count strong")?.textContent === "2",
    "selection was not retained between searches",
  );
  check(true, "multi-selection is preserved while searching other games");
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Revisar jogos selecionados"]',
    )!
    .click();
  await until(
    () => !!document.querySelector(".batch-review-list"),
    "selection review did not open",
  );
  check(
    [...document.querySelectorAll(".batch-review-list strong")]
      .map((el) => el.textContent)
      .join("|") === [elden.name, cyber.name].join("|"),
    "selection review displays the exact future queue order",
  );
  document
    .querySelector<HTMLButtonElement>(`[aria-label="Desmarcar ${cyber.name}"]`)!
    .click();
  await until(
    () => document.querySelectorAll(".batch-review-list > div").length === 1,
    "review removal did not update selection",
  );
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Jogos selecionados"] [aria-label="Fechar diálogo"]',
    )!
    .click();
  await until(
    () => !document.querySelector('[aria-label="Jogos selecionados"]'),
    "selection review did not close",
  );
  document
    .querySelector<HTMLButtonElement>(
      `[data-game-id="${cyber.id}"] .card-batch-toggle`,
    )!
    .click();
  await until(
    () => document.querySelector(".batch-count strong")?.textContent === "2",
    "review selection did not return to library",
  );
  check(
    true,
    "games can be removed from the selection review without touching the queue",
  );
  await edit('[aria-label="Minutos por jogo selecionado"]', "18");
  check(
    document.querySelector<HTMLInputElement>(
      '[aria-label="Duração da seleção por jogo"]',
    )?.value === "18" &&
      document.querySelector(".selection-total strong")?.textContent ===
        "36 min",
    "selection sidebar mirrors the chosen duration and total",
  );
  await openDisplay();
  document
    .querySelector<HTMLSelectElement>("#library-sort")!
    .dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Enter",
        ctrlKey: true,
        bubbles: true,
        cancelable: true,
      }),
    );
  await pause(150);
  check(
    (await state()).playlist.length === before.playlist.length &&
      !!document.querySelector(".batch-composer"),
    "Ctrl Enter cannot submit the selection while display options are open",
  );
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Fechar opções de exibição"]',
    )!
    .click();
  await until(
    () =>
      !document.querySelector<HTMLButtonElement>(
        '[data-testid="add-selected"]',
      )!.disabled,
    "bulk add did not become actionable",
  );
  document
    .querySelector<HTMLButtonElement>('[data-testid="add-selected"]')!
    .click();
  await until(
    () => !document.querySelector(".batch-composer"),
    "completed bulk add did not leave selection mode",
  );
  const added = await state();
  check(
    added.playlist.length === before.playlist.length + 2 &&
      added.playlist
        .slice(-2)
        .map((item) => item.gameId)
        .join("|") === [elden.id, cyber.id].join("|") &&
      added.playlist.slice(-2).every((item) => item.durationMinutes === 18),
    "one bulk action adds all selected games with the requested time and order",
  );
  await request("undoPlaylist");
  check(
    JSON.stringify((await state()).playlist) ===
      JSON.stringify(before.playlist),
    "one undo restores the full queue before a bulk addition",
  );
  await until(
    () =>
      document
        .querySelector(`[data-game-id="${cyber.id}"] .card-queue-badge`)
        ?.textContent?.trim() === String(originalCount),
    "queue badges did not update after undo",
  );
  document
    .querySelector<HTMLButtonElement>('[aria-label="Selecionar vários jogos"]')!
    .click();
  await until(
    () => !!document.querySelector(".card-batch-toggle"),
    "selection mode did not reopen",
  );
  document.querySelector<HTMLButtonElement>(".mark-displayed")!.click();
  await until(
    () =>
      Number(document.querySelector(".batch-count strong")?.textContent) > 0,
    "mark displayed did not select any game",
  );
  check(
    [
      ...document.querySelectorAll<HTMLButtonElement>(".card-batch-toggle"),
    ].every(
      (button) =>
        button.disabled || button.getAttribute("aria-checked") === "true",
    ),
    "mark displayed selects only games compatible with a sequence",
  );
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Cancelar seleção de jogos"]',
    )!
    .click();
  await until(
    () => !document.querySelector(".batch-composer"),
    "cancel did not leave selection mode",
  );
  check(
    JSON.stringify((await state()).playlist) ===
      JSON.stringify(before.playlist),
    "canceling multi-selection leaves the saved queue unchanged",
  );
  document
    .querySelector<HTMLButtonElement>('[aria-label="Selecionar vários jogos"]')!
    .click();
  await until(
    () => !!document.querySelector(".card-batch-toggle"),
    "keyboard selection mode did not reopen",
  );
  document
    .querySelector<HTMLButtonElement>(
      `[data-game-id="${cyber.id}"] .card-batch-toggle`,
    )!
    .click();
  await until(
    () => document.querySelector(".batch-count strong")?.textContent === "1",
    "keyboard selection did not update",
  );
  document.dispatchEvent(
    new KeyboardEvent("keydown", {
      key: "Enter",
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    }),
  );
  await until(
    () => !document.querySelector(".batch-composer"),
    "Ctrl Enter did not add the selection",
  );
  check(
    (await state()).playlist.length === before.playlist.length + 1,
    "Ctrl Enter adds a valid selection without starting playback",
  );
  await request("undoPlaylist");
  await request("configure", { minutes: original.preferences.minutes });
  await edit(".search-input", "");
  await resetLibraryDisplay();
  await until(
    () => !document.querySelector(".catalog-scroll.is-loading"),
    "library did not settle after resetting filters",
  );
  check(
    !document.querySelector(".library-chips") &&
      localStorage.getItem("mirage.librarySort") === "default",
    "reset display clears active chips and restores default order",
  );
  check(
    [
      ...document.querySelectorAll<HTMLElement>(
        ".collection-toolbar,.batch-composer",
      ),
    ].every((el) => el.scrollWidth <= el.clientWidth),
    "library action controls fit the available width",
  );
  return checks;
}

export async function prepareBatchPreview() {
  document
    .querySelector<HTMLButtonElement>('[aria-label="Selecionar vários jogos"]')!
    .click();
  await until(
    () => !!document.querySelector(".card-batch-toggle"),
    "batch preview did not open",
  );
  for (const button of [
    ...document.querySelectorAll<HTMLButtonElement>(
      ".card-batch-toggle:not(:disabled)",
    ),
  ].slice(0, 3)) {
    button.click();
    await pause(60);
  }
}
