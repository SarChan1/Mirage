import { request } from "./bridge";
import type { NativeState, SearchResult } from "./types";
import type { SaveSearchResult } from "./SaveFinder";
import { runConvenienceSmoke } from "./convenience-smoke";
import {
  runLibrarySmoke,
  resetLibraryDisplay,
  prepareBatchPreview,
} from "./library-smoke";
const pause = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
async function until(condition: () => boolean, description: string) {
  for (let i = 0; i < 100; i++) {
    if (condition()) return;
    await pause(50);
  }
  throw new Error(description);
}
export async function runSmoke(scene = "library") {
  const checks: string[] = [];
  const check = (value: unknown, name: string) => {
    if (!value) throw new Error(name);
    checks.push(name);
  };
  const boot = await request<{ state: NativeState; results: SearchResult }>(
    "boot",
  );
  if (!boot.state.preview)
    throw new Error("Smoke tests require isolated preview mode.");
  const language = document.querySelector<HTMLSelectElement>(".language-picker select");
  if (language && language.value !== "pt-BR") {
    language.value = "pt-BR";
    language.dispatchEvent(new Event("change", { bubbles: true }));
    await until(() => document.documentElement.lang === "pt-BR", "Portuguese locale did not restore for smoke tests");
  }
  await resetLibraryDisplay();
  document
    .querySelector<HTMLButtonElement>('[aria-label="Visualizar em grade"]')!
    .click();
  await until(
    () => document.querySelector(".game-grid") !== null,
    "grid did not render",
  );
  check(
    boot.state.catalogCount >= 25000,
    "real catalog loaded through native bridge",
  );
  const search = await request<SearchResult>("search", {
    query: "CYBERPUNK",
    collection: "explore",
  });
  check(
    search.items.some((game) => game.name === "Cyberpunk 2077"),
    "case-insensitive search crosses WebView2 bridge",
  );
  const game = search.items.find((game) => game.supportsPlaylist)!;
  check(Boolean(game), "actual Windows-compatible title found");
  await request("select", { gameId: game.id });
  await until(() => document.querySelector("[data-testid=find-saves]") !== null, "save finder action did not render");
  document.querySelector<HTMLButtonElement>("[data-testid=find-saves]")!.click();
  await until(() => document.querySelector("[data-testid=save-finder]") !== null, "save finder dialog did not open");
  check(document.querySelector('[role=dialog][aria-label="Localizar saves"]') !== null, "save finder opens from game details in the React interface");
  await until(() => document.querySelector('.save-results[aria-busy="false"]') !== null, "save finder did not finish its bounded local scan");
  check(document.querySelector('.save-footer')?.textContent?.includes("Ludusavi"), "save finder shows offline catalog provenance and read-only behavior");
  const saves = await request<SaveSearchResult>("saves.find", { gameId: game.id });
  check(saves.catalogCount > 10000 && saves.catalogName === "Cyberpunk 2077", "save lookup crosses the native bridge and uses exact Steam catalog mapping");
  if (saves.locations.length) {
    check(document.querySelectorAll('.save-copy').length === saves.locations.length, "each save location offers a copy-path action");
    let clipboardBlocked = false;
    try { await request("saves.copy", { id: saves.locations[0].id }); }
    catch { clipboardBlocked = true; }
    check(clipboardBlocked, "preview tests never overwrite the user's clipboard");
  }
  check(document.querySelector('.sidebar') !== null && document.querySelector('[data-testid=find-saves]') !== null, "save search results do not replace the main application state");
  check(document.querySelector<HTMLButtonElement>('.save-toolbar .button')?.textContent?.includes("Escolher pasta"), "manual folder fallback remains available for non-store installations");
  document.querySelector<HTMLButtonElement>('[aria-label="Fechar diálogo"]')!.click();
  await until(() => document.querySelector('[role=dialog][aria-label="Localizar saves"]') === null, "save finder dialog did not close");
  await request("configure", { minutes: 16 });
  await request("select", { gameId: game.id });
  await request("clear");
  let state = await request("add", { gameId: game.id, minutes: 16 });
  const first = state.playlist[0].entryId;
  state = await request("add", { gameId: game.id, minutes: 16 });
  const second = state.playlist[1].entryId;
  check(
    first !== second && state.playlist.length === 2,
    "duplicate games retain distinct entry identities",
  );
  for (const minutes of [0, -1, 1441, 1.5]) {
    let rejected = false;
    try {
      await request("duration", { entryId: second, minutes });
    } catch {
      rejected = true;
    }
    check(rejected, `native boundary rejects duration ${minutes}`);
  }
  state = await request("duration", { entryId: second, minutes: 21 });
  check(
    state.playlist[1].durationMinutes === 21,
    "inline duration updates native state",
  );
  state = await request("move", { entryId: second, delta: -1 });
  check(
    state.playlist[0].entryId === second,
    "reordering updates saved sequence order",
  );
  state = await request("remove", { entryId: first });
  check(
    state.playlist.length === 1 && state.playlist[0].entryId === second,
    "removing one duplicate preserves the other",
  );
  const originalFavorite = state.favorites.includes(game.id);
  state = await request("favorite", { gameId: game.id });
  check(
    state.favorites.includes(game.id) !== originalFavorite,
    "favorite toggle uses C# preferences",
  );
  await request("favorite", { gameId: game.id });
  await until(
    () => document.querySelector("[data-testid=duration-input]") !== null,
    "configuration panel did not render",
  );
  check(
    (document.querySelector("[data-testid=duration-input]") as HTMLInputElement)
      .value === "16",
    "default duration renders as 16",
  );
  const input = document.querySelector<HTMLInputElement>(
    "[data-testid=duration-input]",
  )!;
  input.focus();
  Object.getOwnPropertyDescriptor(
    HTMLInputElement.prototype,
    "value",
  )!.set!.call(input, "18");
  input.dispatchEvent(new Event("input", { bubbles: true }));
  await pause(80);
  input.blur();
  await Promise.resolve();
  const directAdd = document.querySelector<HTMLButtonElement>(
    "[data-testid=inspector-add]",
  )!;
  check(
    !directAdd.disabled,
    "saving a duration on blur keeps the next action clickable",
  );
  directAdd.click();
  await pause(100);
  const afterBlur = (await request<{ state: NativeState }>("boot")).state;
  check(
    afterBlur.playlist.length === 2 &&
      afterBlur.playlist[1].durationMinutes === 18,
    "first click after duration editing adds the intended time",
  );
  await request("remove", { entryId: afterBlur.playlist[1].entryId });
  await request("configure", { minutes: 16 });
  await until(
    () => input.value === "16",
    "default duration did not restore for menu test",
  );
  document.querySelector<HTMLButtonElement>("[data-nav=explore]")!.click();
  await until(
    () => document.querySelector(".card-menu-trigger") !== null,
    "game cards did not render",
  );
  document.querySelector<HTMLButtonElement>(".card-menu-trigger")!.click();
  await until(
    () => document.querySelector("[role=menu]") !== null,
    "vertical menu did not open",
  );
  check(
    Boolean(document.querySelector("[data-testid=menu-add]")),
    "vertical card menu contains add action",
  );
  document.querySelector<HTMLButtonElement>("[data-testid=menu-add]")!.click();
  await until(
    () => document.querySelector("[role=menu]") === null,
    "card menu did not close",
  );
  const afterMenu = (await request<{ state: NativeState }>("boot")).state;
  check(
    afterMenu.playlist.length === 2 &&
      afterMenu.playlist[1].durationMinutes === 16,
    "card menu click adds a 16-minute entry",
  );
  const menuEntry = afterMenu.playlist[1].entryId;
  document.querySelector<HTMLButtonElement>("[data-nav=playlist]")!.click();
  await until(
    () => document.querySelector(`[data-entry-id="${menuEntry}"]`) !== null,
    "sequence view did not render",
  );
  document
    .querySelector<HTMLButtonElement>(
      `[data-entry-id="${menuEntry}"] [data-testid=remove-entry]`,
    )!
    .click();
  await until(
    () => !document.querySelector(`[data-entry-id="${menuEntry}"]`),
    "quick remove did not update list",
  );
  check(true, "row × removes its precise entry through the native bridge");
  document.dispatchEvent(
    new KeyboardEvent("keydown", { key: "k", ctrlKey: true, bubbles: true }),
  );
  await until(
    () => document.activeElement?.classList.contains("search-input") === true,
    "Ctrl K did not focus search",
  );
  check(true, "Ctrl K returns from sequence and focuses search");
  await until(
    () => document.querySelector(".card-menu-trigger") !== null,
    "cards absent after returning from sequence",
  );
  document.querySelector<HTMLButtonElement>(".card-menu-trigger")!.click();
  await until(
    () => document.querySelector("[role=menu]") !== null,
    "menu absent for Escape test",
  );
  document
    .querySelector<HTMLElement>("[role=menu]")!
    .dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true }),
    );
  await until(
    () => !document.querySelector("[role=menu]"),
    "Escape did not close menu",
  );
  check(true, "Escape closes the menu");
  const next = boot.results.items.find(
    (item) => item.supportsPlaylist && item.id !== game.id,
  )!;
  await request("add", { gameId: next.id, minutes: 16 });
  await request("duration", { entryId: second, minutes: 16 });
  await request("configure", { minutes: 16 });
  await pause(250);
  check(
    document.documentElement.scrollWidth <= window.innerWidth,
    "layout has no horizontal overflow",
  );
  const width =
    document.querySelector(".game-card")?.getBoundingClientRect().width ?? 0;
  check(
    width >= 150 && width <= 360,
    "cards remain readable without stretching into oversized bands",
  );
  check(
    !document
      .querySelector(".card-copy")
      ?.textContent?.includes("Pronto para sequência"),
    "cards do not substitute app capabilities for game information",
  );
  document
    .querySelector<HTMLButtonElement>('[aria-label="Visualizar em lista"]')!
    .click();
  await until(
    () => document.querySelector(".game-list .compact") !== null,
    "compact library did not render",
  );
  check(
    document.querySelector(".compact .art")!.getBoundingClientRect().width <
      100,
    "compact library keeps covers small and titles scannable",
  );
  document
    .querySelector<HTMLButtonElement>('[aria-label="Visualizar em grade"]')!
    .click();
  checks.push(...(await runWorkspaceSmoke()));
  checks.push(...(await runLibrarySmoke()));
  checks.push(...(await runConvenienceSmoke()));
  const toast = document.querySelector<HTMLButtonElement>(
    '.toast:not([aria-hidden=true]) [aria-label="Fechar aviso"]',
  );
  toast?.click();
  if (scene === "batch" || scene === "background") await prepareBatchPreview();
  if (scene === "saves") {
    document.querySelector<HTMLButtonElement>('[data-nav="explore"]')!.click();
    await request("select", { gameId: game.id });
    await until(() => document.querySelector('[data-testid=find-saves]') !== null, "save preview action missing");
    document.querySelector<HTMLButtonElement>('[data-testid=find-saves]')!.click();
    await until(() => document.querySelector('.save-results[aria-busy="false"]') !== null, "save preview scan did not finish");
  }
  if (scene === "saved") {
    await request("savePlaylist", { name: "Seleção da noite" });
    await request("durationAll", { minutes: 30 });
    await request("savePlaylist", { name: "Com mais tempo" });
    await request("undoPlaylist");
    document.querySelector<HTMLButtonElement>('[data-nav="playlist"]')!.click();
    await until(
      () => !!document.querySelector('[aria-label="Abrir sequências salvas"]'),
      "saved preview navigation missing",
    );
    document
      .querySelector<HTMLButtonElement>(
        '[aria-label="Abrir sequências salvas"]',
      )!
      .click();
    await until(
      () => !!document.querySelector(".saved-library"),
      "saved preview library missing",
    );
  }
  if (scene.startsWith("save-center")) {
    document.querySelector<HTMLButtonElement>('[data-nav="saves"]')!.click();
    await until(() => document.querySelectorAll(".installed-save-card").length >= 3, "save center preview did not load installed games");
    const saveLocale = scene.endsWith("-en") ? "en" : scene.endsWith("-es") ? "es" : "pt-BR";
    if (saveLocale !== "pt-BR") {
      const picker = document.querySelector<HTMLSelectElement>(".language-picker select")!;
      picker.value = saveLocale; picker.dispatchEvent(new Event("change", { bubbles: true }));
      const heading = saveLocale === "en" ? "Save Center" : "Centro de partidas";
      await until(() => document.querySelector(".page-heading h1")?.textContent === heading, `save center did not translate to ${saveLocale}`);
    }
    check(document.querySelector('[data-testid="save-center"]') !== null, "save center has its own navigation view");
    check(document.querySelectorAll(".save-overview > div").length === 4, "save center presents installation and backup metadata");
    check(document.querySelectorAll<HTMLButtonElement>(".save-backup-card button")[0]?.disabled === true, "preview never writes a save backup");
    check(document.documentElement.scrollWidth <= innerWidth, "save center has no horizontal overflow");
  }
  if (scene.startsWith("locale-")) {
    const target = scene === "locale-en" ? "en" : scene === "locale-es" ? "es" : "pt-BR";
    const picker = document.querySelector<HTMLSelectElement>(".language-picker select")!;
    picker.value = target;
    picker.dispatchEvent(new Event("change", { bubbles: true }));
    await until(() => document.documentElement.lang === target, `locale ${target} did not activate`);
    const expected = target === "en" ? "Sequence" : target === "es" ? "Secuencia" : "Sequência";
    await until(() => document.querySelector('[data-nav="playlist"]')?.textContent?.includes(expected) === true, `locale ${target} did not translate navigation`);
    check(picker.value === target, `locale ${target} remains selected`);
    check(document.documentElement.scrollWidth <= window.innerWidth, `locale ${target} has no horizontal page overflow`);
    const pickerRect = picker.closest(".language-picker")!.getBoundingClientRect();
    check(pickerRect.left >= 0 && pickerRect.right <= innerWidth, `locale ${target} selector fits the title bar`);
    check(localStorage.getItem("mirage.locale") === target, `locale ${target} persists locally`);
    const translatedText = Array.from(document.body.querySelectorAll<HTMLElement>("*")).filter(element => element.offsetParent !== null && !element.closest(".language-picker,[data-i18n-skip],.card-copy h3,.queue-title strong,.game-heading h2,.saved-copy strong,.menu-game,.selection-game-name,.save-game strong")).map(element => element.childElementCount ? "" : element.textContent ?? "").join(" ");
    const portuguese = target === "en"
      ? /\b(sequência|jogos?|biblioteca|duração|configurações|ações|exibição|escolha|seu|sua|processo|inicie|adicionar|detalhes|minutos)\b/i
      : target === "es"
        ? /\b(sequência|jogos?|duração|configurações|ações|exibição|escolha|seu|sua|inicie|adicionar|detalhes)\b/i
        : /$a/;
    check(!portuguese.test(translatedText), `locale ${target} has no visible Portuguese leftovers`);
  }
  return {
    ok: true,
    checks,
    catalogCount: boot.state.catalogCount,
    viewport: { width: innerWidth, height: innerHeight },
    stack: "React + Tailwind CSS + Motion + WebView2 + C#",
  };
}
async function runWorkspaceSmoke() {
  const checks: string[] = [];
  const check = (value: unknown, label: string) => {
    if (!value) throw new Error(label);
    checks.push(label);
  };
  const state = async () =>
    (await request<{ state: NativeState }>("boot")).state;
  const edit = async (selector: string, value: string) => {
    const input = document.querySelector<HTMLInputElement>(selector)!;
    Object.getOwnPropertyDescriptor(
      HTMLInputElement.prototype,
      "value",
    )!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
    await pause(80);
  };
  document.querySelector<HTMLButtonElement>('[data-nav="playlist"]')!.click();
  await until(
    () => !!document.querySelector('[data-testid="save-playlist"]'),
    "queue editor did not render",
  );
  const original = await state(),
    name = `Seleção QA ${Date.now()}`;
  document
    .querySelector<HTMLButtonElement>('[data-testid="save-playlist"]')!
    .click();
  await until(
    () => !!document.querySelector("#saved-name"),
    "save form did not open",
  );
  check(
    document.activeElement?.id === "saved-name",
    "saving a sequence puts keyboard focus in its name field",
  );
  await edit("#saved-name", name);
  document
    .querySelector<HTMLButtonElement>('.sequence-form button[type="submit"]')!
    .click();
  await until(
    () => !document.querySelector("[role=dialog]"),
    "save dialog did not close",
  );
  const saved = (await state()).savedPlaylists.find(
    (item) => item.name === name,
  )!;
  check(
    saved?.count === original.playlist.length,
    "save action stores a named sequence through the native bridge",
  );
  for (const badName of ["", name, "x".repeat(61)]) {
    let rejected = false;
    try {
      await request("savePlaylist", { name: badName });
    } catch {
      rejected = true;
    }
    check(
      rejected,
      "native bridge rejects invalid or duplicate saved sequence name",
    );
  }
  document
    .querySelector<HTMLButtonElement>('[data-testid="save-playlist"]')!
    .click();
  await until(
    () => !!document.querySelector("#saved-name"),
    "duplicate-name form did not open",
  );
  await edit("#saved-name", name);
  document
    .querySelector<HTMLButtonElement>('.sequence-form button[type="submit"]')!
    .click();
  await until(
    () =>
      document
        .querySelector("[role=dialog] .modal-error")
        ?.textContent?.includes("Já existe") === true,
    "duplicate name error was not visible inside its dialog",
  );
  check(true, "form errors remain visible inside the active dialog");
  document.dispatchEvent(
    new KeyboardEvent("keydown", { key: "Escape", bubbles: true }),
  );
  await until(
    () => !document.querySelector("[role=dialog]"),
    "duplicate-name dialog did not close",
  );
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Ajustar duração de todos os jogos"]',
    )!
    .click();
  await until(
    () => !!document.querySelector("#all-minutes"),
    "bulk duration dialog did not open",
  );
  await edit("#all-minutes", "21");
  document
    .querySelector<HTMLButtonElement>('.sequence-form button[type="submit"]')!
    .click();
  await until(
    () => !document.querySelector("[role=dialog]"),
    "bulk duration did not finish",
  );
  check(
    (await state()).playlist.every((item) => item.durationMinutes === 21),
    "bulk duration form updates all current entries",
  );
  check(
    (await state()).savedPlaylists.find((item) => item.id === saved.id)
      ?.totalMinutes === saved.totalMinutes,
    "bulk changes preserve saved template times",
  );
  document
    .querySelector<HTMLButtonElement>('[data-testid="undo-playlist"]')!
    .click();
  await until(
    () =>
      document.querySelector<HTMLInputElement>(".queue-duration input")
        ?.value === String(original.playlist[0].durationMinutes),
    "undo did not restore individual durations",
  );
  check(true, "undo toolbar restores different per-game durations");
  const firstId = original.playlist[0].entryId;
  document
    .querySelector<HTMLButtonElement>(
      `[data-entry-id="${firstId}"] [data-testid="remove-entry"]`,
    )!
    .click();
  await until(
    () =>
      !document.querySelector(`[data-entry-id="${firstId}"]`) &&
      !!document.querySelector<HTMLButtonElement>(
        ".toast:not([aria-hidden=true]) .toast-undo:not(:disabled)",
      ),
    "removal did not offer undo",
  );
  document
    .querySelector<HTMLButtonElement>(
      ".toast:not([aria-hidden=true]) .toast-undo:not(:disabled)",
    )!
    .click();
  await until(
    () => !!document.querySelector(`[data-entry-id="${firstId}"]`),
    "toast undo did not restore removed entry",
  );
  check(
    true,
    "toast action restores a removed entry with its original identity",
  );
  await request("duration", { entryId: firstId, minutes: 24 });
  await until(
    () =>
      document.querySelector<HTMLInputElement>(
        `[data-entry-id="${firstId}"] input`,
      )?.value === "24",
    "duration change did not render",
  );
  document.activeElement instanceof HTMLElement &&
    document.activeElement.blur();
  document.dispatchEvent(
    new KeyboardEvent("keydown", {
      key: "z",
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    }),
  );
  await until(
    () =>
      document.querySelector<HTMLInputElement>(
        `[data-entry-id="${firstId}"] input`,
      )?.value === String(original.playlist[0].durationMinutes),
    "Ctrl Z did not undo queue edit",
  );
  check(true, "Ctrl Z undoes queue edits outside input fields");
  const input = document.querySelector<HTMLInputElement>(
    `[data-entry-id="${firstId}"] input`,
  )!;
  input.focus();
  const key = new KeyboardEvent("keydown", {
    key: "z",
    ctrlKey: true,
    bubbles: true,
    cancelable: true,
  });
  input.dispatchEvent(key);
  check(
    !key.defaultPrevented,
    "text fields retain their own native undo shortcut",
  );
  input.blur();
  document
    .querySelector<HTMLButtonElement>('[aria-label="Abrir sequências salvas"]')!
    .click();
  await until(
    () => !!document.querySelector(`[data-saved-id="${saved.id}"]`),
    "saved library did not render",
  );
  document
    .querySelector<HTMLButtonElement>(
      `[data-saved-id="${saved.id}"] .saved-actions .button`,
    )!
    .click();
  await until(
    () => !!document.querySelector(".load-options"),
    "load choice was not offered for an existing queue",
  );
  check(
    document.querySelectorAll(".load-option").length === 2,
    "loading a saved sequence offers append and replace explicitly",
  );
  document.querySelector<HTMLButtonElement>(".load-option")!.click();
  await until(
    () =>
      document.querySelectorAll("[data-entry-id]").length ===
      original.playlist.length * 2,
    "saved sequence did not append",
  );
  const appended = await state();
  check(
    new Set(appended.playlist.map((item) => item.entryId)).size ===
      appended.playlist.length,
    "appended games have independent identities even when duplicated",
  );
  document
    .querySelector<HTMLButtonElement>('[data-testid="undo-playlist"]')!
    .click();
  await until(
    () =>
      document.querySelectorAll("[data-entry-id]").length ===
      original.playlist.length,
    "append undo failed",
  );
  document
    .querySelector<HTMLButtonElement>('[aria-label="Abrir sequências salvas"]')!
    .click();
  await until(
    () => !!document.querySelector(`[data-saved-id="${saved.id}"]`),
    "saved library did not reopen",
  );
  document
    .querySelector<HTMLButtonElement>(
      `[data-saved-id="${saved.id}"] [title="Renomear"]`,
    )!
    .click();
  await until(
    () => !!document.querySelector("#saved-name"),
    "rename form missing",
  );
  await edit("#saved-name", name + " editada");
  document
    .querySelector<HTMLButtonElement>('.sequence-form button[type="submit"]')!
    .click();
  await until(
    () =>
      document.querySelector(`[data-saved-id="${saved.id}"] h3`)
        ?.textContent ===
      name + " editada",
    "rename did not update library",
  );
  check(true, "saved sequence can be renamed without modifying its games");
  document
    .querySelector<HTMLButtonElement>(
      `[data-saved-id="${saved.id}"] [title="Excluir sequência salva"]`,
    )!
    .click();
  await until(
    () =>
      !![...document.querySelectorAll("[role=dialog] h2")].find(
        (el) => el.textContent === "Excluir sequência salva?",
      ),
    "deletion confirmation missing",
  );
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Excluir sequência salva?"] .button.primary',
    )!
    .click();
  await until(
    () => !document.querySelector(`[data-saved-id="${saved.id}"]`),
    "saved sequence was not deleted",
  );
  check(
    (await state()).playlist.length === original.playlist.length,
    "deleting a saved sequence keeps the current queue intact",
  );
  await until(
    () => document.querySelector('[aria-label="Sequências salvas"]') !== null,
    "saved library did not return after deletion",
  );
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Sequências salvas"] [aria-label="Fechar diálogo"]',
    )!
    .click();
  await until(
    () => !document.querySelector("[role=dialog]"),
    "saved library did not close",
  );
  document
    .querySelector<HTMLButtonElement>('[aria-label="Limpar sequência"]')!
    .click();
  await until(
    () => !!document.querySelector('[aria-label="Limpar sua sequência?"]'),
    "clear confirmation missing",
  );
  document
    .querySelector<HTMLButtonElement>(
      '[aria-label="Limpar sua sequência?"] .button.primary',
    )!
    .click();
  await until(
    () => document.querySelectorAll("[data-entry-id]").length === 0,
    "queue did not clear",
  );
  await until(
    () => !document.querySelector("[role=dialog]"),
    "clear dialog did not close",
  );
  document
    .querySelector<HTMLButtonElement>('[data-testid="undo-playlist"]')!
    .click();
  await until(
    () =>
      document.querySelectorAll("[data-entry-id]").length ===
      original.playlist.length,
    "undo clear did not restore queue",
  );
  check(true, "clear sequence can be undone from its empty state");
  check(
    /\d{2}:\d{2}/.test(
      document.querySelector('[data-testid="finish-estimate"]')?.textContent ??
        "",
    ),
    "queue displays a local estimated completion time",
  );
  check(
    [
      ...document.querySelectorAll<HTMLElement>(
        ".queue-toolbar,.queue-edit-tools",
      ),
    ].every((el) => el.scrollWidth <= el.clientWidth),
    "new queue controls fit the available width",
  );
  document.querySelector<HTMLButtonElement>('[data-nav="explore"]')!.click();
  await until(
    () => !!document.querySelector(".game-grid"),
    "library did not return after workspace tests",
  );
  return checks;
}
export async function runRuntimeSmoke() {
  const checks: string[] = [];
  const state = await request("previewSession");
  if (!state.preview || !state.session?.isPlaylist)
    throw new Error("Isolated running scene was not created.");
  checks.push("running scene uses in-memory state without starting a game");
  const entryId = state.playlist[0].entryId;
  for (const command of [
    "remove",
    "duration",
    "clear",
    "move",
    "add",
    "undoPlaylist",
    "durationAll",
    "savePlaylist",
    "usePlaylist",
    "renamePlaylist",
    "deletePlaylist",
    "addMany",
  ]) {
    let rejected = false;
    try {
      await request(command, {
        entryId,
        gameId: state.selected!.id,
        minutes: 16,
        delta: 1,
      });
    } catch {
      rejected = true;
    }
    if (!rejected) throw new Error(`Running sequence accepted ${command}.`);
    checks.push(`running sequence blocks ${command}`);
  }
  document.querySelector<HTMLButtonElement>("[data-nav=playlist]")!.click();
  await until(
    () => document.querySelector("[data-testid=runtime-panel]") !== null,
    "Runtime panel absent.",
  );
  await until(
    () =>
      document.querySelectorAll("[data-testid=remove-entry]").length ===
      state.playlist.length,
    "Running rows absent.",
  );
  if (
    ![
      ...document.querySelectorAll<HTMLButtonElement>(
        "[data-testid=remove-entry]",
      ),
    ].every((button) => button.disabled)
  )
    throw new Error("Running removal controls must be disabled.");
  checks.push("running view hides configuration and disables removal");
  if (document.querySelector("[data-testid=inspector-add]"))
    throw new Error("Disabled composer should not occupy the runtime panel.");
  if (
    !/^\d{2}:\d{2}/.test(
      document.querySelector("[data-testid=countdown]")?.textContent ?? "",
    )
  )
    throw new Error("Countdown missing.");
  checks.push("countdown and next item render from native session");
  if (
    state.playlistTiming.remainingSeconds <= 0 ||
    state.playlistTiming.remainingSeconds >
      state.playlistTiming.totalMinutes * 60
  )
    throw new Error("Remaining queue time is invalid.");
  checks.push("total remaining time excludes elapsed stages");
  return { ok: true, checks };
}
