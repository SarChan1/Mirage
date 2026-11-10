import {
  useCallback,
  lazy,
  useMemo,
  useEffect,
  useRef,
  useState,
  Suspense,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";
import { AnimatePresence, motion, useIsPresent } from "motion/react";
import {
  ArrowDown,
  ArrowRight,
  ArrowUp,
  Check,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Clock3,
  Command,
  Heart,
  HelpCircle,
  History,
  LibraryBig,
  LayoutGrid,
  List,
  ListMusic,
  LoaderCircle,
  Minus,
  MoreVertical,
  FolderOpen,
  Save,
  Pencil,
  Undo2,
  CheckSquare2,
  SlidersHorizontal,
  Play,
  Plus,
  RefreshCw,
  Search,
  Settings2,
  Languages,
  Sparkles,
  Square,
  Trash2,
  X,
} from "lucide-react";
import { request, subscribe } from "./bridge";
import { clock, duration, finishTime, validMinutes } from "./format.mjs";
import "./sequences.css";
import "./library.css";
import { LibraryOptions, type DisplayAnchor } from "./LibraryOptions";
import { SelectionInspector } from "./SelectionInspector";
import type { QuickAction } from "./QuickActions";
import { localeOptions, useI18n, type Locale } from "./i18n";
import type {
  Game,
  NativeState,
  QueueEntry,
  SearchResult,
  SavedPlaylist,
  View,
  SortOrder,
} from "./types";

const SaveCenter = lazy(() => import("./SaveCenter").then(module => ({ default: module.SaveCenter })));
const SaveFinderPanel = lazy(() => import("./SaveFinder").then(module => ({ default: module.SaveFinderPanel })));
const QuickActions = lazy(() => import("./QuickActions").then(module => ({ default: module.QuickActions })));
const PreferencesPanel = lazy(() => import("./Preferences").then(module => ({ default: module.PreferencesPanel })));
const panelFallback = <div className="modal-loading"><LoaderCircle className="spin" size={18} /><span>Preparando…</span></div>;

type Action = (
  command: string,
  payload?: Record<string, unknown>,
  notice?: string,
) => Promise<NativeState | undefined>;
type Notice = { id: number; text: string; error?: boolean; undo?: boolean };
type Dialog =
  | "help"
  | "clear"
  | "save"
  | "saved"
  | "rename"
  | "deleteSaved"
  | "loadSaved"
  | "durationAll"
  | "batchReview"
  | "saves"
  | "commands"
  | "preferences"
  | "replaceStorage"
  | null;
const queueNotices: Record<string, string> = {
  add: "Jogo adicionado à sequência.",
  remove: "Jogo removido da sequência.",
  move: "Ordem da sequência alterada.",
  duration: "Duração atualizada.",
  durationAll: "Duração de todos os jogos atualizada.",
  clear: "Sequência limpa.",
  usePlaylist: "Sequência salva carregada.",
  addMany: "Jogos adicionados à sequência.",
};
function Logo() {
  return (
    <svg viewBox="0 0 32 34" fill="none" aria-hidden="true">
      <path
        d="M3 25 9.5 7 16 19 22.5 7 29 25"
        stroke="currentColor"
        strokeWidth="2.6"
        strokeLinejoin="round"
        strokeLinecap="round"
      />
      <circle cx="16" cy="29" r="1.6" fill="currentColor" />
    </svg>
  );
}
function Toast({
  notice,
  canUndo,
  locked,
  onUndo,
  onClose,
}: {
  notice: Notice;
  canUndo: boolean;
  locked: boolean;
  onUndo: () => void;
  onClose: () => void;
}) {
  const present = useIsPresent();
  return (
    <motion.div
      className={`toast ${notice.error ? "is-error" : ""}`}
      role={notice.error ? "alert" : "status"}
      aria-hidden={!present || undefined}
      style={{ pointerEvents: present ? "auto" : "none" }}
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: 8 }}
    >
      {notice.error ? <HelpCircle size={17} /> : <CheckCircle2 size={17} />}
      <span>{notice.text}</span>
      {notice.undo && canUndo && (
        <button
          className="toast-undo"
          disabled={locked || !present}
          onClick={onUndo}
        >
          Desfazer
        </button>
      )}
      <button
        onClick={onClose}
        className="icon-button"
        aria-label="Fechar aviso"
        disabled={!present}
      >
        <X size={14} />
      </button>
    </motion.div>
  );
}
function Art({
  game,
  className = "",
}: {
  game: Pick<Game, "heroUrl" | "iconUrl" | "initial" | "name">;
  className?: string;
}) {
  const [failed, setFailed] = useState(false);
  const [iconFailed, setIconFailed] = useState(false);
  useEffect(() => {
    setFailed(false);
    setIconFailed(false);
  }, [game.heroUrl, game.iconUrl]);
  return (
    <div className={`art ${className}`}>
      <div className="art-fallback">
        <span>{game.initial}</span>
        {game.iconUrl && !iconFailed && (
          <img
            src={game.iconUrl}
            alt=""
            loading="lazy"
            onError={() => setIconFailed(true)}
          />
        )}
      </div>
      {game.heroUrl && !failed && (
        <img
          className="art-cover"
          src={game.heroUrl}
          alt=""
          loading="lazy"
          onError={() => setFailed(true)}
        />
      )}
      <div className="art-shade" />
    </div>
  );
}
function WindowBar() {
  const { locale, setLocale } = useI18n();
  const [open, setOpen] = useState(false);
  const picker = useRef<HTMLDivElement>(null);
  const current = localeOptions.find(option => option.value === locale)!;
  useEffect(() => {
    if (!open) return;
    const close = (event: PointerEvent) => { if (!picker.current?.contains(event.target as Node)) setOpen(false); };
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") setOpen(false); };
    document.addEventListener("pointerdown", close);
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("pointerdown", close); document.removeEventListener("keydown", escape); };
  }, [open]);
  return (
    <div className="window-bar">
      <div className="window-caption">
        <span className="caption-dot" /> Mirage{" "}
        <span className="caption-separator">/</span>
        <span>Desktop</span>
      </div>
      <div className="window-actions">
        <div className="language-picker" title="Idioma" ref={picker} data-i18n-skip>
          <select className="language-native-proxy" aria-hidden="true" tabIndex={-1} value={locale} onChange={event => setLocale(event.target.value as Locale)}>
            {localeOptions.map(option => <option key={option.value} value={option.value}>{option.short} · {option.label}</option>)}
          </select>
          <button className="language-trigger" aria-label="Idioma" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(value => !value)}>
            <Languages size={13} aria-hidden="true" /><span className="language-code">{current.short}</span><span className="language-name">{current.label}</span><ChevronDown size={12} className={open ? "is-open" : ""} />
          </button>
          {open && <div className="language-menu" role="menu" aria-label="Idioma">
            <div className="language-menu-title">Idioma</div>
            {localeOptions.map(option => <button role="menuitemradio" aria-checked={locale === option.value} key={option.value} onClick={() => { setLocale(option.value); setOpen(false); }}><span className="language-option-code">{option.short}</span><span>{option.label}</span>{locale === option.value && <Check size={13} />}</button>)}
          </div>}
        </div>
        <div className="window-buttons">
        <button
          aria-label="Minimizar"
          onClick={() => void request("window.minimize")}
        >
          <Minus size={14} />
        </button>
        <button
          aria-label="Maximizar ou restaurar"
          onClick={() => void request("window.maximize")}
        >
          <Square size={11} />
        </button>
        <button
          aria-label="Fechar janela"
          className="window-close"
          onClick={() => void request("window.close")}
        >
          <X size={16} />
        </button>
        </div>
      </div>
    </div>
  );
}

function Modal({
  title,
  children,
  onClose,
  error,
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
  error?: string;
}) {
  const present = useIsPresent();
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement;
    (
      ref.current?.querySelector<HTMLElement>("[data-autofocus]") ??
      ref.current?.querySelector<HTMLElement>("button")
    )?.focus();
    return () => { if (!document.querySelector('[role="dialog"][aria-modal="true"]')) previous?.focus(); };
  }, []);
  return createPortal(
    <motion.div
      className="modal-shade"
      aria-hidden={!present || undefined}
      style={{ pointerEvents: present ? "auto" : "none" }}
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      onPointerDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <motion.div
        className="modal"
        role="dialog"
        aria-modal={present ? "true" : undefined}
        aria-label={title}
        ref={ref}
        initial={{ opacity: 0, y: 12, scale: 0.98 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        exit={{ opacity: 0, y: 6 }}
        onKeyDown={(e) => {
          if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) e.stopPropagation();
          if (e.key === "Escape") {
            e.preventDefault();
            e.stopPropagation();
            onClose();
          }
          if (e.key === "Tab") {
            const items = [
              ...ref.current!.querySelectorAll<HTMLElement>(
                "button:not(:disabled),input:not(:disabled),a[href]",
              ),
            ].filter(item => item.tabIndex >= 0 && !item.hidden);
            const first = items[0],
              last = items.at(-1);
            if (e.shiftKey && document.activeElement === first) {
              e.preventDefault();
              last?.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
              e.preventDefault();
              first?.focus();
            }
          }
        }}
      >
        <div className="modal-heading">
          <h2>{title}</h2>
          <button
            className="icon-button"
            onClick={onClose}
            aria-label="Fechar diálogo"
          >
            <X size={18} />
          </button>
        </div>
        {error && (
          <p className="modal-error" role="alert">
            {error}
          </p>
        )}
        {children}
      </motion.div>
    </motion.div>,
    document.body,
  );
}

function ContextMenu({
  anchor,
  onClose,
  onAdd,
  onFavorite,
  favorite,
  locked,
  minutes,
}: {
  anchor: { game: Game; x: number; y: number; origin: HTMLElement };
  onClose: () => void;
  onAdd: () => void;
  onFavorite: () => void;
  favorite: boolean;
  locked: boolean;
  minutes: number;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    ref.current
      ?.querySelector<HTMLButtonElement>("button:not(:disabled)")
      ?.focus();
    const close = (e: PointerEvent) => {
      if (!ref.current?.contains(e.target as Node)) onClose();
    };
    const scroll = (e: Event) => {
      if (!ref.current?.contains(e.target as Node)) onClose();
    };
    document.addEventListener("pointerdown", close);
    window.addEventListener("resize", onClose);
    document.addEventListener("scroll", scroll, true);
    return () => {
      document.removeEventListener("pointerdown", close);
      window.removeEventListener("resize", onClose);
      document.removeEventListener("scroll", scroll, true);
    };
  }, [onClose]);
  return createPortal(
    <motion.div
      ref={ref}
      className="context-menu"
      role="menu"
      aria-label={`Opções de ${anchor.game.name}`}
      initial={{ opacity: 0, y: -4, scale: 0.97 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, scale: 0.97 }}
      style={{
        top: Math.min(anchor.y, innerHeight - 150),
        left: Math.max(10, Math.min(anchor.x - 220, innerWidth - 240)),
      }}
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          e.stopPropagation();
          onClose();
          anchor.origin.focus();
        }
        if (e.key === "ArrowDown" || e.key === "ArrowUp") {
          e.preventDefault();
          const buttons = [
            ...ref.current!.querySelectorAll<HTMLButtonElement>(
              "button:not(:disabled)",
            ),
          ];
          const i = buttons.indexOf(
            document.activeElement as HTMLButtonElement,
          );
          buttons[
            (i + (e.key === "ArrowDown" ? 1 : -1) + buttons.length) %
              buttons.length
          ]?.focus();
        }
      }}
    >
      <div className="menu-game">{anchor.game.name}</div>
      <button
        role="menuitem"
        disabled={locked || !anchor.game.supportsPlaylist}
        onClick={onAdd}
        data-testid="menu-add"
      >
        <Plus size={16} />
        <span>Adicionar à sequência</span>
        <span className="menu-shortcut">{minutes}m</span>
      </button>
      <button role="menuitem" onClick={onFavorite}>
        <Heart size={15} fill={favorite ? "currentColor" : "none"} />
        {favorite ? "Remover dos favoritos" : "Guardar nos favoritos"}
      </button>
      {!anchor.game.supportsPlaylist && <p>Sem processo compatível no catálogo.</p>}
      {locked && <p>Aguarde o fim da sessão para editar.</p>}
    </motion.div>,
    document.body,
  );
}

function GameCard({
  game,
  selected,
  favorite,
  onSelect,
  onMenu,
  compact,
  selecting,
  batchSelected,
  queuedCount,
  selectionLocked,
}: {
  game: Game;
  selected: boolean;
  favorite: boolean;
  onSelect: () => void;
  onMenu: (event: React.MouseEvent<HTMLButtonElement>) => void;
  compact: boolean;
  selecting: boolean;
  batchSelected: boolean;
  queuedCount: number;
  selectionLocked: boolean;
}) {
  return (
    <motion.article
      layout
      className={`game-card ${selected && !selecting ? "is-selected" : ""} ${compact ? "compact" : ""} ${batchSelected ? "is-batch-selected" : ""}`}
      transition={{ duration: 0.18 }}
      data-game-id={game.id}
    >
      <button
        className="card-select"
        tabIndex={selecting ? -1 : 0}
        onClick={onSelect}
        disabled={selecting && (!game.supportsPlaylist || selectionLocked)}
        aria-label={`Selecionar ${game.name}`}
        aria-pressed={selecting ? batchSelected : selected}
        title={
          selecting && !game.supportsPlaylist
            ? "Sem processo compatível"
            : undefined
        }
      >
        <Art game={game} />
        <div className="card-copy">
          <h3 title={game.name}>{game.name}</h3>
        </div>
      </button>
      {favorite && (
        <span className="card-favorite" aria-label="Favorito">
          <Heart size={12} fill="currentColor" />
        </span>
      )}
      <button
        className="card-menu-trigger"
        aria-label={`Opções de ${game.name}`}
        aria-haspopup="menu"
        onClick={onMenu}
      >
        <MoreVertical size={17} />
      </button>
      {queuedCount > 0 && (
        <span
          className={`card-queue-badge ${favorite ? "with-favorite" : ""}`}
          aria-label={`${queuedCount} ${queuedCount === 1 ? "entrada" : "entradas"} na sequência`}
          title={`${queuedCount} ${queuedCount === 1 ? "entrada" : "entradas"} na sequência`}
        >
          <ListMusic size={11} />
          {queuedCount}
        </span>
      )}
      {selecting && (
        <button
          className={`card-batch-toggle ${batchSelected ? "checked" : ""}`}
          role="checkbox"
          aria-checked={batchSelected}
          aria-label={`Selecionar ${game.name} para a sequência`}
          disabled={!game.supportsPlaylist || selectionLocked}
          onClick={onSelect}
        >
          {batchSelected && <Check size={13} />}
        </button>
      )}
      {selected && !selecting && (
        <span className="card-selected-mark">
          <Check size={11} />
        </span>
      )}
    </motion.article>
  );
}

function QueueRow({
  entry,
  index,
  count,
  locked,
  act,
}: {
  entry: QueueEntry;
  index: number;
  count: number;
  locked: boolean;
  act: Action;
}) {
  const [minutes, setMinutes] = useState(String(entry.durationMinutes));
  useEffect(
    () => setMinutes(String(entry.durationMinutes)),
    [entry.durationMinutes],
  );
  const save = () => {
    if (validMinutes(minutes)) {
      if (Number(minutes) !== entry.durationMinutes)
        void act("duration", {
          entryId: entry.entryId,
          minutes: Number(minutes),
        });
    } else setMinutes(String(entry.durationMinutes));
  };
  return (
    <motion.li
      layout
      initial={{ opacity: 0, y: 5 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, x: -12 }}
      className={`queue-row ${entry.status === "playing" ? "is-playing" : ""} ${entry.status === "done" ? "is-done" : ""}`}
      data-entry-id={entry.entryId}
    >
      <span className="queue-index">
        {entry.status === "done" ? (
          <Check size={15} />
        ) : (
          String(index + 1).padStart(2, "0")
        )}
      </span>
      <div className="queue-icon">
        {entry.iconUrl ? (
          <img src={entry.iconUrl} alt="" />
        ) : (
          <ListMusic size={20} />
        )}
      </div>
      <div className="queue-title">
        <h3 title={entry.gameName}>{entry.gameName}</h3>
        <span>
          {entry.status === "playing" ? (
            <>
              <span className="playing-dot" />
              Em reprodução
            </>
          ) : entry.status === "done" ? (
            "Concluído"
          ) : locked ? (
            "A seguir"
          ) : entry.showWindow ? (
            "Com janela auxiliar"
          ) : (
            "Em segundo plano"
          )}
        </span>
      </div>
      <div className="row-order">
        <button
          aria-label={`Mover ${entry.gameName} para cima`}
          className="icon-button"
          disabled={locked || index === 0}
          onClick={() =>
            void act("move", { entryId: entry.entryId, delta: -1 })
          }
        >
          <ArrowUp size={14} />
        </button>
        <button
          aria-label={`Mover ${entry.gameName} para baixo`}
          className="icon-button"
          disabled={locked || index === count - 1}
          onClick={() => void act("move", { entryId: entry.entryId, delta: 1 })}
        >
          <ArrowDown size={14} />
        </button>
      </div>
      <label className="queue-duration">
        <input
          type="number"
          min={1}
          max={1440}
          step={1}
          value={minutes}
          disabled={locked}
          aria-label={`Minutos de ${entry.gameName}`}
          onChange={(e) => setMinutes(e.target.value)}
          onBlur={save}
          onKeyDown={(e) => {
            if (e.key === "Enter") e.currentTarget.blur();
          }}
        />
        <span>min</span>
      </label>
      <button
        className="icon-button remove-row"
        data-testid="remove-entry"
        disabled={locked}
        aria-label={`Remover ${entry.gameName}`}
        title={
          locked ? "Encerre a sequência para editar" : "Remover da sequência"
        }
        onClick={() =>
          void act(
            "remove",
            { entryId: entry.entryId },
            "Jogo removido da sequência.",
          )
        }
      >
        <X size={16} />
      </button>
    </motion.li>
  );
}

function RuntimePanel({
  state,
  act,
  onQueue,
}: {
  state: NativeState;
  act: Action;
  onQueue: () => void;
}) {
  const session = state.session!;
  return (
    <motion.section
      className="runtime-panel"
      key="runtime"
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      data-testid="runtime-panel"
    >
      <div className="section-kicker">
        <span className="playing-dot" />
        EM REPRODUÇÃO
        <span className="runtime-position">
          {String(session.index + 1).padStart(2, "0")}
          <i>/</i>
          {String(session.count).padStart(2, "0")}
        </span>
      </div>
      <div className="runtime-card">
        <Art game={session.game} className="runtime-art" />
        <div className="runtime-copy">
          <h2>{session.game.name}</h2>
          <span className="runtime-label">TEMPO RESTANTE</span>
          <div className="countdown" data-testid="countdown">
            {clock(session.remainingSeconds)}
          </div>
          <div className="progress-track">
            <motion.div
              animate={{ width: `${session.itemProgress * 100}%` }}
              transition={{ duration: 0.7, ease: "linear" }}
            />
          </div>
          <div className="runtime-duration">
            <span>de {duration(session.durationMinutes)}</span>
            <span>Troca automática</span>
          </div>
        </div>
      </div>
      <div className="next-game">
        <div className="next-game-icon">
          <ArrowRight size={18} />
        </div>
        <div>
          <span className="eyebrow">A SEGUIR</span>
          <h3>{session.next?.name ?? "Esta é a última etapa"}</h3>
          <p>
            {session.next
              ? `${duration(session.next.minutes)} · início automático`
              : "A sequência termina ao concluir este jogo."}
          </p>
        </div>
      </div>
      <button className="button primary w-full" onClick={onQueue}>
        Acompanhar sequência
        <ArrowRight size={15} />
      </button>
      <button
        className="button danger-ghost w-full"
        disabled={state.busy}
        onClick={() => void act("stop")}
      >
        <Square size={12} fill="currentColor" />
        Encerrar sequência
      </button>
      <p className="panel-footnote">
        <Clock3 size={12} />
        Continua mesmo com a janela minimizada.
      </p>
    </motion.section>
  );
}

function Inspector({
  state,
  act,
  minutes,
  setMinutes,
  locked,
  onQueue,
  onFindSaves,
}: {
  state: NativeState;
  act: Action;
  minutes: string;
  setMinutes: (value: string) => void;
  locked: boolean;
  onQueue: () => void;
  onFindSaves: () => void;
}) {
  const game = state.selected;
  const isFavorite = game ? state.favorites.includes(game.id) : false;
  return (
    <aside className="inspector">
      <div className="inspector-top">
        <span>Detalhes</span>
        <span className="connection">
          <i />
          Processo local
        </span>
      </div>
      <div className="inspector-scroll">
        <AnimatePresence mode="wait">
          {state.session?.isPlaylist ? (
            <RuntimePanel state={state} act={act} onQueue={onQueue} />
          ) : game ? (
            <motion.section
              key="configuration"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              className="configuration"
            >
              <div className="selected-art">
                <Art game={game} />
                <button
                  className={`favorite-button ${isFavorite ? "is-favorite" : ""}`}
                  aria-label={
                    isFavorite ? "Remover favorito" : "Favoritar jogo"
                  }
                  onClick={() => void act("favorite", { gameId: game.id })}
                >
                  <Heart
                    size={17}
                    fill={isFavorite ? "currentColor" : "none"}
                  />
                </button>
              </div>
              <div className="game-heading">
                <h2>{game.name}</h2>
              </div>
              <button className="find-saves-button" onClick={onFindSaves} data-testid="find-saves"><span><FolderOpen size={15} /><span><strong>Localizar saves</strong><small>Ver locais e detalhes</small></span></span><ChevronRight size={14} /></button>
              <div className="field-section-title">Processo</div>
              <p className="mode-description">
                Inicie um jogo ou adicione à sua sequência.
              </p>
                  <details className="advanced-options">
                    <summary>
                      <Settings2 size={14} />
                      Opções do processo
                      <ChevronDown size={14} />
                    </summary>
                    <div className="advanced-content">
                      {game.executables.length ? (
                        <>
                          <label className="field-label" htmlFor="variant">
                            Executável
                          </label>
                          <select
                            id="variant"
                            value={state.variantIndex}
                            disabled={locked}
                            onChange={(e) =>
                              void act("variant", {
                                index: Number(e.target.value),
                              })
                            }
                          >
                            {game.executables.map((path, i) => (
                              <option key={path} value={i}>
                                {path}
                              </option>
                            ))}
                          </select>
                          <label className="check-label">
                            <input
                              type="checkbox"
                              checked={state.preferences.showWindow}
                              disabled={locked}
                              onChange={(e) =>
                                void act("configure", {
                                  showWindow: e.target.checked,
                                })
                              }
                            />
                            <span>Janela auxiliar minimizada</span>
                          </label>
                          <p>O reconhecimento pode ser conferido no Discord.</p>
                        </>
                      ) : (
                        <p>
                          Este título não possui um processo compatível no catálogo.
                        </p>
                      )}
                    </div>
                  </details>
                  <div className="duration-section">
                    <div className="duration-heading">
                      <h3>Duração na sequência</h3>
                      <span>minutos</span>
                    </div>
                    <div className="duration-stepper">
                      <button
                        className="step-button"
                        aria-label="Diminuir cinco minutos"
                        disabled={locked}
                        onClick={() =>
                          setMinutes(
                            String(Math.max(1, (Number(minutes) || 16) - 5)),
                          )
                        }
                      >
                        <Minus size={16} />
                      </button>
                      <input
                        data-testid="duration-input"
                        aria-label="Duração em minutos"
                        inputMode="numeric"
                        type="number"
                        min={1}
                        max={1440}
                        value={minutes}
                        disabled={locked}
                        onChange={(e) => setMinutes(e.target.value)}
                        onBlur={() => {
                          if (validMinutes(minutes))
                            void act("configure", { minutes: Number(minutes) });
                        }}
                      />
                      <button
                        className="step-button"
                        aria-label="Aumentar cinco minutos"
                        disabled={locked}
                        onClick={() =>
                          setMinutes(
                            String(Math.min(1440, (Number(minutes) || 16) + 5)),
                          )
                        }
                      >
                        <Plus size={16} />
                      </button>
                    </div>
                    <div className="duration-presets">
                      {[16, 30, 60].map((value) => (
                        <button
                          key={value}
                          className={Number(minutes) === value ? "active" : ""}
                          disabled={locked}
                          onClick={() => {
                            setMinutes(String(value));
                            void act("configure", { minutes: value });
                          }}
                        >
                          {value} min
                        </button>
                      ))}
                    </div>
                  </div>
                  <button
                    className="button primary w-full"
                    data-testid="inspector-add"
                    disabled={
                      locked || !game.supportsPlaylist || !validMinutes(minutes)
                    }
                    onClick={() =>
                      void act("add", {
                        gameId: game.id,
                        minutes: Number(minutes),
                      })
                    }
                  >
                    <Plus size={17} />
                    Adicionar à sequência
                  </button>
                  <button className="queue-link" onClick={onQueue}>
                    Minha sequência<span>{state.playlist.length}</span>
                    <ArrowRight size={13} />
                  </button>
                  <div className="action-divider">
                    <span>ou</span>
                  </div>
                  <button
                    className="button secondary w-full"
                    disabled={locked || !game.supportsPlaylist}
                    onClick={() => void act("activate")}
                  >
                    <Play size={14} fill="currentColor" />
                    Iniciar só este jogo
                  </button>
              {state.session && !state.session.isPlaylist && (
                <div className="individual-session">
                  <div className="individual-top">
                    <span className="playing-dot" />
                    {state.session.confirmed
                      ? "Sessão ativa"
                      : "Preparando atividade"}
                    <span>{clock(state.session.elapsedSeconds)}</span>
                  </div>
                  <p>{state.session.game.name}</p>
                  <button
                    className="button danger-ghost w-full"
                    disabled={state.busy}
                    onClick={() => void act("stop")}
                  >
                    Encerrar sessão
                  </button>
                </div>
              )}
            </motion.section>
          ) : (
            <div className="inspector-empty">
              <Sparkles size={30} />
              <h3>Seu próximo jogo começa na biblioteca.</h3>
              <p>Selecione um título para preparar a atividade.</p>
            </div>
          )}
        </AnimatePresence>
      </div>
      <div className="inspector-bottom">
        <span className="local-dot" />
        Salvo neste computador
      </div>
    </aside>
  );
}

export default function App() {
  const { locale } = useI18n();
  const restored = window.__MIRAGE_VIEW_STATE__;
  const [state, setState] = useState<NativeState | null>(null);
  const [error, setError] = useState("");
  const [view, setView] = useState<View>(restored?.view ?? "explore");
  const lastCatalogView = useRef<View>(restored?.lastCatalogView ?? "explore");
  const [query, setQuery] = useState(restored?.query ?? "");
  const [onlySequence, setOnlySequence] = useState(restored?.onlySequence ?? false);
  const [hideQueued, setHideQueued] = useState(restored?.hideQueued ?? false);
  const [sort, setSort] = useState<SortOrder>(() => {
    const value = localStorage.getItem("mirage.librarySort");
    return value === "nameAsc" || value === "nameDesc" ? value : "default";
  });
  const [displayAnchor, setDisplayAnchor] = useState<DisplayAnchor | null>(
    null,
  );
  const closeDisplay = useCallback(() => setDisplayAnchor(null), []);
  const [selecting, setSelecting] = useState(restored?.selecting ?? false);
  const [selection, setSelection] = useState<Map<string, Game>>(() => new Map((restored?.selection ?? []).map(game => [game.id, game])));
  const queuedCounts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const item of state?.playlist ?? [])
      counts.set(item.gameId, (counts.get(item.gameId) ?? 0) + 1);
    return counts;
  }, [state?.playlist]);
  const queueFingerprint = [...queuedCounts.keys()].sort().join(",");
  const [layout, setLayout] = useState<"grid" | "list">(() =>
    localStorage.getItem("mirage.libraryLayout") === "list" ? "list" : "grid",
  );
  const [results, setResults] = useState<SearchResult>({
    items: [],
    total: 0,
    hasMore: false,
  });
  const [loading, setLoading] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [pending, setPending] = useState(0);
  const [revision, setRevision] = useState(0);
  const [minutes, setMinutes] = useState(restored?.minutes ?? "16");
  const restoredMinutesPending = useRef(Boolean(restored));
  const [menu, setMenu] = useState<{
    game: Game;
    x: number;
    y: number;
    origin: HTMLElement;
  } | null>(null);
  const [modal, setModal] = useState<Dialog>(null);
  const [savedSelection, setSavedSelection] = useState<SavedPlaylist | null>(
    null,
  );
  const [savedName, setSavedName] = useState("");
  const [allMinutes, setAllMinutes] = useState("16");
  const [dialogError, setDialogError] = useState("");
  useEffect(() => setDialogError(""), [modal]);
  const [notice, setNotice] = useState<Notice | null>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    window.__MIRAGE_CAPTURE_VIEW__ = () => ({ view, lastCatalogView: lastCatalogView.current, query, onlySequence, hideQueued, selecting, selection: [...selection.values()], minutes, preferenceMinutes: state?.preferences.minutes ?? 16 });
    return () => { delete window.__MIRAGE_CAPTURE_VIEW__; };
  }, [view, query, onlySequence, hideQueued, selecting, selection, minutes, state?.preferences.minutes]);
  const searchGeneration = useRef(0);
  const lastStatus = useRef("");
  const notify = useCallback(
    (text: string, error = false, undo = false) =>
      setNotice({ id: Date.now(), text, error, undo }),
    [],
  );
  const closeMenu = useCallback(() => setMenu(null), []);
  const ready = state !== null;
  useEffect(() => {
    localStorage.setItem("mirage.libraryLayout", layout);
  }, [layout]);
  useEffect(() => {
    localStorage.setItem("mirage.librarySort", sort);
  }, [sort]);
  useEffect(() => {
    const off = subscribe(setState);
    void request<{ state: NativeState; results: SearchResult }>("boot")
      .then((boot) => {
        setState(boot.state);
        setResults(boot.results);
      })
      .catch((e) => setError(e.message));
    return off;
  }, []);
  useEffect(() => {
    if (state) {
      if (restoredMinutesPending.current) {
        restoredMinutesPending.current = false;
        if (restored && restored.minutes !== String(restored.preferenceMinutes)) return;
      }
      setMinutes(String(state.preferences.minutes));
    }
  }, [state?.preferences.minutes]);
  useEffect(() => {
    let active = true;
    if (state?.preview) {
      window.__MIRAGE_READY__ = false;
      void import("./smoke").then(module => {
        if (!active) return;
        window.__MIRAGE_SMOKE__ = module.runSmoke;
        window.__MIRAGE_RUNTIME_SMOKE__ = module.runRuntimeSmoke;
        window.__MIRAGE_READY__ = ready;
      });
    } else {
      window.__MIRAGE_READY__ = ready;
    }
    return () => {
      active = false;
      window.__MIRAGE_READY__ = false;
    };
  }, [ready, state?.preview]);
  useEffect(() => {
    if (!state?.status) return;
    const key = state.status.title + state.status.description;
    if (lastStatus.current === key) return;
    lastStatus.current = key;
    notify(
      state.status.title +
        (state.status.description ? ` · ${state.status.description}` : ""),
      /não|interrompida|inválid/i.test(state.status.title),
    );
  }, [state?.status?.title, state?.status?.description, notify]);
  useEffect(() => {
    if (notice) {
      const id = setTimeout(
        () => setNotice(null),
        notice.error || notice.undo ? 6500 : 3800,
      );
      return () => clearTimeout(id);
    }
  }, [notice]);
  const navigate = (next: View) => {
    if (next === "explore" || next === "favorites" || next === "recent") lastCatalogView.current = next;
    setView(next);
    closeMenu();
    closeDisplay();
  };
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if (!e.repeat && (e.ctrlKey || e.metaKey) && e.shiftKey && e.key.toLowerCase() === "p" && (!modal || modal === "commands")) {
        e.preventDefault(); closeMenu(); closeDisplay(); setModal(modal === "commands" ? null : "commands"); return;
      }
      if (!e.repeat && (e.ctrlKey || e.metaKey) && e.key === "," && (!modal || modal === "preferences")) {
        e.preventDefault(); closeMenu(); closeDisplay(); setModal(modal === "preferences" ? null : "preferences"); return;
      }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k" && !modal) {
        e.preventDefault();
        setView(lastCatalogView.current);
        setTimeout(() => {
          searchRef.current?.focus();
          searchRef.current?.select();
        }, 0);
      }
      if (e.key === "Escape") {
        if (modal) setModal(null);
        else if (menu) setMenu(null);
        else if (displayAnchor) {
          closeDisplay();
          displayAnchor.origin.focus();
        } else if (document.activeElement === searchRef.current) {
          if (query) setQuery("");
          else searchRef.current?.blur();
        } else if (selecting && pending === 0 && !state?.busy) {
          setSelecting(false);
          setSelection(new Map());
        }
      }
    };
    document.addEventListener("keydown", key);
    return () => document.removeEventListener("keydown", key);
  }, [
    menu,
    modal,
    query,
    displayAnchor,
    selecting,
    closeDisplay,
    pending,
    state?.busy,
  ]);
  useEffect(() => {
    if (!ready || view === "playlist" || view === "saves") return;
    const generation = ++searchGeneration.current;
    setLoading(true);
    const timer = setTimeout(
      () => {
        void request<SearchResult>("search", {
          query,
          collection: view,
          onlySequence,
          hideQueued,
          sort,
        })
          .then((result) => {
            if (generation === searchGeneration.current) setResults(result);
          })
          .catch((e) => notify(e.message, true))
          .finally(() => {
            if (generation === searchGeneration.current) setLoading(false);
          });
      },
      query ? 90 : 0,
    );
    return () => {
      clearTimeout(timer);
      ++searchGeneration.current;
    };
  }, [
    query,
    view,
    onlySequence,
    hideQueued,
    sort,
    hideQueued ? queueFingerprint : "",
    ready,
    revision,
    state?.catalogCount,
    notify,
  ]);
  const act: Action = async (command, payload = {}, message) => {
    const blocking = ![
      "configure",
      "select",
      "favorite",
      "variant",
      "duration",
    ].includes(command);
    if (blocking) setPending((n) => n + 1);
    try {
      const result = await request<NativeState>(command, payload);
      setState(result);
      if (!result.storage.warning && (message || queueNotices[command]))
        notify(
          message ?? queueNotices[command],
          false,
          Boolean(queueNotices[command] && result.canUndoPlaylist),
        );
      if (command === "favorite" || command === "refresh")
        setRevision((n) => n + 1);
      return result;
    } catch (e) {
      if (modal && document.querySelector('[role="dialog"][aria-modal="true"]')) setDialogError((e as Error).message);
      else notify((e as Error).message, true);
    } finally {
      if (blocking) setPending((n) => n - 1);
    }
  };
  const toggleSelection = (game: Game) => {
    if (!game.supportsPlaylist || state?.busy || pending > 0) return;
    if (!selection.has(game.id) && selection.size >= 100) {
      notify("Selecione até 100 jogos por vez.", true);
      return;
    }
    setSelection((previous) => {
      const next = new Map(previous);
      if (next.has(game.id)) next.delete(game.id);
      else {
        if (next.size >= 100) return previous;
        next.set(game.id, game);
      }
      return next;
    });
  };
  const markDisplayed = () => {
    if (state?.busy || pending > 0) return;
    const eligible = results.items.filter((game) => game.supportsPlaylist);
    const allMarked =
      eligible.length > 0 && eligible.every((game) => selection.has(game.id));
    const next = new Map(selection);
    if (allMarked) for (const game of eligible) next.delete(game.id);
    else
      for (const game of eligible) {
        if (next.size >= 100 && !next.has(game.id)) {
          notify(
            "Limite de 100 jogos selecionados. Revise a seleção antes de adicionar.",
          );
          break;
        }
        next.set(game.id, game);
      }
    setSelection(next);
  };
  const addSelection = async () => {
    if (
      !selection.size ||
      !validMinutes(minutes) ||
      state?.busy ||
      pending ||
      state?.session?.isPlaylist
    )
      return;
    const result = await act("addMany", {
      gameIds: [...selection.keys()],
      minutes: Number(minutes),
    });
    if (result) {
      setSelection(new Map());
      setSelecting(false);
      setModal(null);
    }
  };
  useEffect(() => {
    const key = (event: KeyboardEvent) => {
      if (
        selecting &&
        !modal &&
        !displayAnchor &&
        !event.repeat &&
        event.key === "Enter" &&
        (event.ctrlKey || event.metaKey)
      ) {
        event.preventDefault();
        void addSelection();
      }
    };
    document.addEventListener("keydown", key);
    return () => document.removeEventListener("keydown", key);
  }, [
    selecting,
    selection,
    minutes,
    modal,
    displayAnchor,
    state?.busy,
    state?.session?.isPlaylist,
    pending,
  ]);
  useEffect(() => {
    const undo = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement;
      if (
        event.key.toLowerCase() === "z" &&
        (event.ctrlKey || event.metaKey) &&
        !event.shiftKey &&
        !modal &&
        state?.canUndoPlaylist &&
        !state.busy &&
        pending === 0 &&
        !target.closest?.("input,textarea,select,[contenteditable=true]")
      ) {
        event.preventDefault();
        void act("undoPlaylist", {}, "Alteração desfeita.");
      }
    };
    document.addEventListener("keydown", undo);
    return () => document.removeEventListener("keydown", undo);
  }, [modal, state?.canUndoPlaylist, state?.busy, pending]);
  const more = async () => {
    const generation = searchGeneration.current;
    setLoadingMore(true);
    try {
      const result = await request<SearchResult>("search", {
        query,
        collection: view,
        onlySequence,
        hideQueued,
        sort,
        offset: results.items.length,
      });
      if (generation === searchGeneration.current)
        setResults((old) => ({
          ...result,
          items: [...old.items, ...result.items],
        }));
    } catch (e) {
      notify((e as Error).message, true);
    } finally {
      setLoadingMore(false);
    }
  };
  if (!state)
    return (
      <div className="app-shell">
        <WindowBar />
        <div className="splash">
          <div className="splash-logo">
            <Logo />
          </div>
          <h1>mirage</h1>
          {error ? (
            <>
              <p className="startup-error">{error}</p>
              <button
                className="button secondary"
                onClick={() => location.reload()}
              >
                Tentar novamente
              </button>
            </>
          ) : (
            <>
              <p>Preparando seu espaço de jogo</p>
              <LoaderCircle className="spin" size={20} />
            </>
          )}
        </div>
      </div>
    );
  const locked = state.busy || pending > 0;
  const queueRunning = Boolean(state.session?.isPlaylist);
  const editingLocked = locked || queueRunning;
  const totalMinutes = state.playlist.reduce(
    (sum, entry) => sum + entry.durationMinutes,
    0,
  );
  const title =
    view === "playlist"
      ? "Minha sequência"
      : view === "saves"
        ? "Central de saves"
      : view === "favorites"
        ? "Seus favoritos"
        : view === "recent"
          ? "Jogos recentes"
          : "Biblioteca";
  const subtitle =
    view === "playlist"
      ? "Organize os jogos, ajuste os tempos e siga sua ordem."
      : view === "saves"
        ? "Encontre, confira e proteja seus saves em um só lugar."
      : view === "favorites"
        ? "Seus jogos preferidos, sempre por perto."
        : view === "recent"
          ? "Volte de onde parou."
          : "Encontre um jogo. Prepare sua próxima atividade.";
  const navItems = [
    { id: "explore" as View, label: "Biblioteca", Icon: LibraryBig },
    {
      id: "favorites" as View,
      label: "Favoritos",
      Icon: Heart,
      count: state.favorites.length,
    },
    { id: "recent" as View, label: "Recentes", Icon: History },
  ];
  const navigateFromCommand = (next: View) => { setModal(null); navigate(next); };
  const quickActions: QuickAction[] = [
    { id: "search", title: "Pesquisar jogos", detail: "Voltar para a busca da biblioteca", shortcut: "Ctrl K", keywords: "encontrar catálogo", run: () => {
      setModal(null); navigate(lastCatalogView.current);
      let attempts = 0;
      const focus = () => { if (document.querySelector('[role="dialog"]')) { if (++attempts < 40) requestAnimationFrame(focus); return; } searchRef.current?.focus(); searchRef.current?.select(); };
      requestAnimationFrame(focus);
    } },
    { id: "library", title: "Abrir biblioteca", detail: "Explorar o catálogo completo", run: () => navigateFromCommand("explore") },
    { id: "favorites", title: "Abrir favoritos", detail: "Seus jogos guardados", run: () => navigateFromCommand("favorites") },
    { id: "recent", title: "Abrir recentes", detail: "Voltar aos últimos jogos usados", run: () => navigateFromCommand("recent") },
    { id: "queue", title: "Abrir sequência", detail: `${state.playlist.length} jogos na lista atual`, keywords: "lista playlist", run: () => navigateFromCommand("playlist") },
    { id: "saved", title: "Abrir sequências salvas", detail: "Reutilizar uma seleção de jogos", keywords: "listas modelos", disabled: editingLocked, run: () => setModal("saved") },
    { id: "save-center", title: "Abrir Central de saves", detail: "Jogos instalados, datas e backups", keywords: "saves backup zip instalado", run: () => navigateFromCommand("saves") },
    { id: "add", title: "Adicionar jogo selecionado", detail: state.selected ? `${state.selected.name} · ${minutes} min` : "Selecione um jogo na biblioteca", disabled: editingLocked || !state.selected?.supportsPlaylist || !validMinutes(minutes), run: () => { setModal(null); if (state.selected) void act("add", { gameId: state.selected.id, minutes: Number(minutes) }); } },
    { id: "saves", title: "Localizar saves", detail: state.selected?.name ?? "Selecione um jogo na biblioteca", keywords: "pasta arquivos salvamento", disabled: !state.selected, run: () => setModal("saves") },
    { id: "undo", title: "Desfazer alteração da sequência", detail: state.undoLabel || "Nenhuma alteração para desfazer", shortcut: "Ctrl Z", disabled: editingLocked || !state.canUndoPlaylist, run: () => { setModal(null); void act("undoPlaylist", {}, "Alteração desfeita."); } },
    { id: "layout", title: layout === "grid" ? "Usar visualização em lista" : "Usar visualização em grade", detail: "Mudar a apresentação da biblioteca", keywords: "cards capas layout", run: () => { setModal(null); navigate(lastCatalogView.current); setLayout(layout === "grid" ? "list" : "grid"); } },
    { id: "preferences", title: "Abrir configurações", detail: "Duração padrão, bandeja e avisos", keywords: "ajustes opções notificações", shortcut: "Ctrl ,", run: () => setModal("preferences") },
  ];
  return (
    <div className="app-shell">
      <WindowBar />
      <div className="workspace">
        <nav className="sidebar" aria-label="Navegação principal">
          <div className="brand">
            <Logo />
            <span>
              mirage<span className="brand-period">.</span>
            </span>
          </div>
          <div className="brand-description">Seu espaço de atividade</div>
          <div className="nav-section-title">Coleção</div>
          <div className="navigation">
            {navItems.map(({ id, label, Icon, count }) => (
              <button
                key={id}
                className={`nav-button ${view === id ? "active" : ""}`}
                data-nav={id}
                aria-current={view === id ? "page" : undefined}
                onClick={() => navigate(id)}
              >
                {view === id && (
                  <motion.span
                    className="nav-highlight"
                    layoutId="navigation"
                    transition={{ type: "spring", stiffness: 400, damping: 36 }}
                  />
                )}
                <Icon size={18} />
                <span>{label}</span>
                {Boolean(count) && <small>{count}</small>}
              </button>
            ))}
            <div className="nav-divider" />
            <button
              className={`nav-button ${view === "playlist" ? "active" : ""}`}
              data-nav="playlist"
              aria-current={view === "playlist" ? "page" : undefined}
              onClick={() => navigate("playlist")}
            >
              {view === "playlist" && (
                <motion.span className="nav-highlight" layoutId="navigation" />
              )}
              <ListMusic size={18} />
              <span>Sequência</span>
              {state.playlist.length > 0 && (
                <small className={queueRunning ? "playing-count" : ""}>
                  {state.playlist.length}
                </small>
              )}
            </button>
            <button
              className={`nav-button ${view === "saves" ? "active" : ""}`}
              data-nav="saves"
              aria-current={view === "saves" ? "page" : undefined}
              onClick={() => navigate("saves")}
            >
              {view === "saves" && <motion.span className="nav-highlight" layoutId="navigation" />}
              <FolderOpen size={18} />
              <span>Central de saves</span>
            </button>
          </div>
          <div className="sidebar-bottom">
            <button
              className="utility-button"
              disabled={locked}
              onClick={() => void act("refresh")}
            >
              <RefreshCw size={15} className={state.busy ? "spin" : ""} />
              Atualizar catálogo
            </button>
            <button className="utility-button" onClick={() => setModal("commands")} data-testid="open-commands" title="Ctrl Shift P"><Command size={15} /> Ações rápidas</button>
            <button className="utility-button" onClick={() => setModal("preferences")} data-testid="open-preferences" title="Ctrl ,"><Settings2 size={15} /> Configurações</button>
            <button className="utility-button" onClick={() => setModal("help")}>
              <HelpCircle size={15} />
              Como funciona
            </button>
            <div className="sidebar-footer">
              <span className="local-dot" />
              <span>Mirage para Windows</span>
              <small>{state.version}</small>
            </div>
          </div>
        </nav>
        <main className="main-pane">
          {state.storage.warning && <div className="storage-warning" role="alert" data-testid="storage-warning">
            <div><strong>Suas alterações ainda não foram salvas</strong><p>{state.storage.warning}</p></div>
            <button className="button secondary small" disabled={locked} onClick={() => state.storage.replacementRequired ? setModal("replaceStorage") : void act("retrySave")}>{state.storage.replacementRequired ? "Resolver" : "Tentar salvar"}</button>
          </div>}
          <header className="page-heading">
            <div className="eyebrow">
              Mirage <ChevronRight size={12} />
              <span>{title}</span>
            </div>
            <div className="heading-line">
              <div>
                <h1>{title}</h1>
                <p>{subtitle}</p>
              </div>
              {view === "playlist" && (
                <span className={`page-badge ${queueRunning ? "is-live" : ""}`}>
                  <span />
                  {queueRunning ? "Em reprodução" : "Sua seleção"}
                </span>
              )}
            </div>
          </header>
          {view === "playlist" ? (
            <div className="queue-scroll">
              <div className="queue-toolbar">
                <div>
                  <span className="queue-total">
                    {state.playlist.length.toString().padStart(2, "0")}
                  </span>
                  <div>
                    <strong>
                      {state.playlist.length === 1
                        ? "jogo na sequência"
                        : "jogos na sequência"}
                    </strong>
                    <span>
                      <Clock3 size={12} />
                      {duration(totalMinutes)} no total
                    </span>
                  </div>
                </div>
                <div>
                  <button
                    className="button secondary small"
                    aria-label="Abrir sequências salvas"
                    onClick={() => setModal("saved")}
                  >
                    <FolderOpen size={14} />
                    <span>
                      Salvas
                      {state.savedPlaylists.length > 0
                        ? ` (${state.savedPlaylists.length})`
                        : ""}
                    </span>
                  </button>
                  <button
                    className="button secondary small"
                    onClick={() => navigate(lastCatalogView.current)}
                  >
                    <Plus size={14} />
                    Adicionar jogos
                  </button>
                </div>
              </div>
              {!queueRunning && (
                <div
                  className="queue-edit-tools"
                  role="group"
                  aria-label="Editar sequência"
                >
                  <button
                    className="button ghost small"
                    aria-label="Desfazer alteração na sequência"
                    title={
                      state.undoLabel
                        ? `Desfazer: ${state.undoLabel} · Ctrl Z`
                        : "Nenhuma alteração para desfazer"
                    }
                    disabled={editingLocked || !state.canUndoPlaylist}
                    onClick={() =>
                      void act("undoPlaylist", {}, "Alteração desfeita.")
                    }
                    data-testid="undo-playlist"
                  >
                    <Undo2 size={14} />
                    Desfazer
                  </button>
                  <button
                    className="button ghost small"
                    aria-label="Ajustar duração de todos os jogos"
                    disabled={editingLocked || state.playlist.length === 0}
                    onClick={() => {
                      setAllMinutes(String(state.preferences.minutes));
                      setModal("durationAll");
                    }}
                  >
                    <Clock3 size={14} />
                    Durações
                  </button>
                  <button
                    className="button ghost small"
                    aria-label="Salvar sequência"
                    disabled={editingLocked || state.playlist.length === 0}
                    onClick={() => {
                      setSavedName("");
                      setModal("save");
                    }}
                    data-testid="save-playlist"
                  >
                    <Save size={14} />
                    Salvar
                  </button>
                  <span />
                  <button
                    className="button ghost small"
                    disabled={editingLocked || state.playlist.length === 0}
                    onClick={() => setModal("clear")}
                    aria-label="Limpar sequência"
                  >
                    <Trash2 size={13} />
                    Limpar
                  </button>
                </div>
              )}
              {queueRunning && (
                <div className="queue-now">
                  <span className="playing-dot" />
                  <span>{state.session!.game.name}</span>
                  <strong>
                    {clock(state.session!.remainingSeconds)} restantes
                  </strong>
                </div>
              )}
              {state.playlist.length ? (
                <>
                  <motion.ol
                    className="queue-list"
                    layout
                    data-testid="queue-list"
                  >
                    <AnimatePresence initial={false}>
                      {state.playlist.map((entry, index) => (
                        <QueueRow
                          key={entry.entryId}
                          entry={entry}
                          index={index}
                          count={state.playlist.length}
                          locked={editingLocked}
                          act={act}
                        />
                      ))}
                    </AnimatePresence>
                  </motion.ol>
                  <div className="queue-footer">
                    <p>
                      <Clock3 size={14} />
                      <span
                        className="finish-estimate"
                        data-testid="finish-estimate"
                      >
                        {queueRunning
                          ? "Término previsto"
                          : "Se começar agora, termina"}
                        <strong>
                          {finishTime(state.playlistTiming.remainingSeconds, Date.now(), locale)}
                        </strong>
                      </span>
                    </p>
                    <button
                      className={`button ${queueRunning ? "secondary" : "primary"}`}
                      disabled={locked}
                      onClick={() =>
                        void act(queueRunning ? "stop" : "startPlaylist")
                      }
                      data-testid="start-playlist"
                    >
                      {queueRunning ? (
                        <Square size={12} fill="currentColor" />
                      ) : (
                        <Play size={14} fill="currentColor" />
                      )}
                      {queueRunning ? "Parar sequência" : "Iniciar sequência"}
                    </button>
                  </div>
                </>
              ) : (
                <div className="empty-state queue-empty">
                  <div className="empty-symbol">
                    <ListMusic size={30} />
                  </div>
                  <h2>Um jogo depois do outro.</h2>
                  <p>
                    Escolha os títulos na biblioteca e defina o tempo de cada
                    um. O Mirage segue sua ordem.
                  </p>
                  <button
                    className="button primary"
                    onClick={() => navigate("explore")}
                  >
                    Explorar jogos
                    <ArrowRight size={15} />
                  </button>
                </div>
              )}
            </div>
          ) : view === "saves" ? (
            <Suspense fallback={<div className="empty-state"><LoaderCircle className="spin" size={22} /><p>Preparando Central de saves…</p></div>}>
              <SaveCenter preview={state.preview} onInspect={async gameId => { await act("select", { gameId }); setModal("saves"); }} />
            </Suspense>
          ) : (
            <>
              <div className="search-box">
                <Search size={19} />
                <input
                  ref={searchRef}
                  className="search-input"
                  data-testid="search-input"
                  placeholder="Buscar em todos os jogos…"
                  aria-label="Buscar jogo"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                />
                {query ? (
                  <button
                    className="icon-button"
                    aria-label="Limpar busca"
                    onClick={() => {
                      setQuery("");
                      searchRef.current?.focus();
                    }}
                  >
                    <X size={15} />
                  </button>
                ) : (
                  <kbd>Ctrl K</kbd>
                )}
              </div>
              <div className="collection-toolbar">
                <h2>
                  {query
                    ? "Resultados da busca"
                    : view === "favorites"
                      ? "Guardados por você"
                      : view === "recent"
                        ? "Sua última seleção"
                        : "Todos os jogos"}
                  <span>{results.total.toLocaleString(locale)}</span>
                  {loading && <LoaderCircle size={13} className="spin" />}
                </h2>
                <div className="collection-actions">
                  <button
                    className="button ghost small library-select-mode"
                    aria-label={
                      selecting
                        ? "Cancelar seleção de jogos"
                        : "Selecionar vários jogos"
                    }
                    aria-pressed={selecting}
                    disabled={locked || (queueRunning && !selecting)}
                    onClick={() => {
                      setSelecting(!selecting);
                      setSelection(new Map());
                      closeDisplay();
                      closeMenu();
                    }}
                  >
                    <CheckSquare2 size={14} />
                    {selecting ? "Cancelar" : "Selecionar"}
                  </button>
                  <button
                    className={`button ghost small display-trigger ${onlySequence || hideQueued || sort !== "default" ? "active" : ""}`}
                    aria-label="Opções de exibição da biblioteca"
                    aria-expanded={Boolean(displayAnchor)}
                    aria-haspopup="dialog"
                    onClick={(event) => {
                      const rect = event.currentTarget.getBoundingClientRect();
                      setDisplayAnchor(
                        displayAnchor
                          ? null
                          : {
                              x: rect.right,
                              y: rect.bottom + 7,
                              origin: event.currentTarget,
                            },
                      );
                      closeMenu();
                    }}
                  >
                    <SlidersHorizontal size={14} />
                    Exibição
                    {(onlySequence || hideQueued || sort !== "default") && (
                      <span />
                    )}
                  </button>
                  <span className="toolbar-separator" />
                  <div
                    className="view-switch"
                    role="group"
                    aria-label="Visualização da biblioteca"
                  >
                    <button
                      aria-label="Visualizar em grade"
                      aria-pressed={layout === "grid"}
                      onClick={() => setLayout("grid")}
                    >
                      <LayoutGrid size={15} />
                    </button>
                    <button
                      aria-label="Visualizar em lista"
                      aria-pressed={layout === "list"}
                      onClick={() => setLayout("list")}
                    >
                      <List size={16} />
                    </button>
                  </div>
                </div>
              </div>
              {(onlySequence || hideQueued || sort !== "default") && (
                <div
                  className="library-chips"
                  aria-label="Filtros e ordenação ativos"
                >
                  {onlySequence && (
                    <button
                      onClick={() => setOnlySequence(false)}
                      aria-label="Remover filtro de sequência"
                    >
                      Com sequência
                      <X size={11} />
                    </button>
                  )}
                  {hideQueued && (
                    <button
                      onClick={() => setHideQueued(false)}
                      aria-label="Mostrar jogos já na lista"
                    >
                      Fora da lista
                      <X size={11} />
                    </button>
                  )}
                  {sort !== "default" && (
                    <button
                      onClick={() => setSort("default")}
                      aria-label="Restaurar ordenação padrão"
                    >
                      {sort === "nameAsc" ? "Nome A–Z" : "Nome Z–A"}
                      <X size={11} />
                    </button>
                  )}
                  <button
                    className="clear-library-filters"
                    onClick={() => {
                      setOnlySequence(false);
                      setHideQueued(false);
                      setSort("default");
                    }}
                  >
                    Limpar
                  </button>
                </div>
              )}
              {selecting && (
                <div className="batch-composer" data-testid="batch-composer">
                  <button
                    className="batch-count"
                    disabled={selection.size === 0 || locked}
                    onClick={() => setModal("batchReview")}
                    aria-label="Revisar jogos selecionados"
                  >
                    <CheckSquare2 size={15} />
                    <strong>{selection.size}</strong>{" "}
                    {selection.size === 1 ? "selecionado" : "selecionados"}
                    <ChevronRight size={12} />
                  </button>
                  <button
                    className="button ghost small mark-displayed"
                    disabled={locked}
                    onClick={markDisplayed}
                  >
                    {results.items.filter((game) => game.supportsPlaylist)
                      .length > 0 &&
                    results.items
                      .filter((game) => game.supportsPlaylist)
                      .every((game) => selection.has(game.id))
                      ? "Desmarcar exibidos"
                      : "Marcar exibidos"}
                  </button>
                  <div className="batch-controls">
                    <label className="batch-duration">
                      <input
                        type="number"
                        min={1}
                        max={1440}
                        step={1}
                        aria-label="Minutos por jogo selecionado"
                        disabled={locked}
                        value={minutes}
                        onChange={(event) => setMinutes(event.target.value)}
                      />
                      <span>min/jogo</span>
                    </label>
                    <button
                      className="button primary small"
                      data-testid="add-selected"
                      disabled={
                        editingLocked ||
                        !selection.size ||
                        !validMinutes(minutes) ||
                        state.playlist.length + selection.size > 500
                      }
                      title="Adicionar seleção · Ctrl Enter"
                      onClick={() => void addSelection()}
                    >
                      <Plus size={14} />
                      Adicionar {selection.size || "jogos"}
                    </button>
                  </div>
                  {state.playlist.length + selection.size > 500 && (
                    <p className="selection-limit">
                      A seleção ultrapassa o limite de 500 jogos da lista.
                    </p>
                  )}
                </div>
              )}
              <div
                className={`catalog-scroll ${loading ? "is-loading" : ""}`}
                data-testid="catalog-scroll"
              >
                {results.items.length ? (
                  <>
                    <div
                      className={layout === "grid" ? "game-grid" : "game-list"}
                    >
                      {results.items.map((game) => (
                        <GameCard
                          key={game.id}
                          game={game}
                          selected={game.id === state.selected?.id}
                          favorite={state.favorites.includes(game.id)}
                          compact={layout === "list"}
                          selecting={selecting}
                          batchSelected={selection.has(game.id)}
                          queuedCount={queuedCounts.get(game.id) ?? 0}
                          selectionLocked={locked}
                          onSelect={() =>
                            selecting
                              ? toggleSelection(game)
                              : void act("select", { gameId: game.id })
                          }
                          onMenu={(e) => {
                            e.stopPropagation();
                            const rect =
                              e.currentTarget.getBoundingClientRect();
                            setMenu({
                              game,
                              x: rect.right,
                              y: rect.bottom + 6,
                              origin: e.currentTarget,
                            });
                          }}
                        />
                      ))}
                    </div>
                    {results.hasMore && (
                      <div className="load-more">
                        <button
                          className="button secondary small"
                          disabled={loadingMore || loading}
                          onClick={() => void more()}
                        >
                          {loadingMore ? (
                            <LoaderCircle className="spin" size={14} />
                          ) : (
                            <Plus size={14} />
                          )}
                          Carregar mais jogos
                        </button>
                      </div>
                    )}
                  </>
                ) : (
                  !loading && (
                    <div className="empty-state">
                      <div className="empty-symbol">
                        {view === "favorites" ? (
                          <Heart size={28} />
                        ) : (
                          <Search size={28} />
                        )}
                      </div>
                      <h2>
                        {onlySequence || hideQueued
                          ? "Nenhum jogo corresponde aos filtros."
                          : query
                            ? "Nenhum jogo por aqui."
                            : view === "favorites"
                              ? "Guarde o que você gosta."
                              : "Sua coleção começa aqui."}
                      </h2>
                      <p>
                        {onlySequence || hideQueued
                          ? "Remova os filtros para ver os jogos desta coleção."
                          : query
                            ? "Tente outro nome ou use uma palavra mais curta."
                            : view === "favorites"
                              ? "Use o coração para manter seus jogos preferidos por perto."
                              : "Os jogos que você usar vão aparecer aqui."}
                      </p>
                      {(onlySequence || hideQueued) && (
                        <button
                          className="button secondary small"
                          onClick={() => {
                            setOnlySequence(false);
                            setHideQueued(false);
                          }}
                        >
                          Limpar filtros
                        </button>
                      )}
                      {query && (
                        <button
                          className="button secondary small"
                          onClick={() => setQuery("")}
                        >
                          Limpar busca
                        </button>
                      )}
                    </div>
                  )
                )}
              </div>
              <footer className="catalog-footer">
                <span>
                  <span className="local-dot" />
                  Disponível offline
                </span>
                <span>{state.catalogCount.toLocaleString(locale)} jogos</span>
              </footer>
            </>
          )}
        </main>
        {view === "saves" ? null : selecting && !queueRunning ? (
          <SelectionInspector
            games={[...selection.values()]}
            minutes={minutes}
            setMinutes={setMinutes}
            locked={locked}
            overLimit={state.playlist.length + selection.size > 500}
            onRemove={toggleSelection}
            onReview={() => setModal("batchReview")}
            onAdd={() => void addSelection()}
            onCancel={() => {
              setSelecting(false);
              setSelection(new Map());
            }}
          />
        ) : (
          <Inspector
            state={state}
            act={act}
            minutes={minutes}
            setMinutes={setMinutes}
            locked={locked}
            onQueue={() => navigate("playlist")}
            onFindSaves={() => setModal("saves")}
          />
        )}
      </div>
      {displayAnchor && (
        <LibraryOptions
          anchor={displayAnchor}
          sort={sort}
          onlySequence={onlySequence}
          hideQueued={hideQueued}
          onSort={setSort}
          onSequence={setOnlySequence}
          onHideQueued={setHideQueued}
          onReset={() => {
            setSort("default");
            setOnlySequence(false);
            setHideQueued(false);
          }}
          onClose={closeDisplay}
        />
      )}
      <AnimatePresence>
        {menu && (
          <ContextMenu
            anchor={menu}
            onClose={closeMenu}
            favorite={state.favorites.includes(menu.game.id)}
            minutes={validMinutes(minutes) ? Number(minutes) : 16}
            locked={editingLocked || !validMinutes(minutes)}
            onAdd={() => {
              void act("add", {
                gameId: menu.game.id,
                minutes: Number(minutes),
              });
              closeMenu();
            }}
            onFavorite={() => {
              void act("favorite", { gameId: menu.game.id });
              closeMenu();
            }}
          />
        )}
      </AnimatePresence>
      <AnimatePresence>
        {notice && (
          <Toast
            key={notice.id}
            notice={notice}
            canUndo={state.canUndoPlaylist}
            locked={editingLocked}
            onUndo={() => void act("undoPlaylist", {}, "Alteração desfeita.")}
            onClose={() => setNotice(null)}
          />
        )}
      </AnimatePresence>
      <AnimatePresence>
        {modal === "commands" && <Modal key="commands" title="Ações rápidas" onClose={() => setModal(null)}><Suspense fallback={panelFallback}><QuickActions actions={quickActions} /></Suspense></Modal>}
        {modal === "preferences" && <Modal key="preferences" title="Configurações" error={dialogError} onClose={() => setModal(null)}><Suspense fallback={panelFallback}><PreferencesPanel preferences={state.preferences} storageWarning={state.storage.warning} onChange={async values => Boolean(await act("configure", values))} /></Suspense></Modal>}
        {modal === "replaceStorage" && <Modal key="replaceStorage" title="Recuperar o salvamento" error={dialogError} onClose={() => setModal(null)}>
          <p className="modal-intro">O arquivo de preferências não pôde ser lido. Para tentar recuperá-lo manualmente, feche o Mirage e confira preferences.json em %LOCALAPPDATA%\Mirage.</p>
          <p className="modal-intro">Você também pode substituí-lo pelas configurações e sequências desta sessão. Os dados antigos que não puderam ser lidos serão perdidos.</p>
          <div className="modal-actions"><button className="button secondary" onClick={() => setModal(null)}>Manter arquivo original</button><button className="button primary" disabled={locked} onClick={async () => { const result = await act("replaceUnreadablePreferences"); if (result && !result.storage.warning) setModal(null); else if (result) setDialogError(result.storage.warning ?? "Não foi possível salvar."); }}>Substituir pelo estado atual</button></div>
        </Modal>}
        {modal === "saves" && state.selected && <Modal key="saves" title="Localizar saves" onClose={() => setModal(null)}><Suspense fallback={panelFallback}><SaveFinderPanel game={state.selected} /></Suspense></Modal>}
        {modal === "batchReview" && (
          <Modal
            key="batchReview"
            title="Jogos selecionados"
            error={dialogError}
            onClose={() => setModal(null)}
          >
            <p className="modal-intro">
              {selection.size} {selection.size === 1 ? "jogo" : "jogos"} ·{" "}
              {validMinutes(minutes)
                ? duration(selection.size * Number(minutes))
                : "Defina um tempo válido"}{" "}
              no total. A ordem segue sua seleção.
            </p>
            <div className="batch-review-list">
              {[...selection.values()].map((game, index) => (
                <div key={game.id}>
                  <span className="batch-review-index">
                    {String(index + 1).padStart(2, "0")}
                  </span>
                  <strong>{game.name}</strong>
                  <button
                    className="icon-button"
                    aria-label={`Desmarcar ${game.name}`}
                    disabled={locked}
                    onClick={() => toggleSelection(game)}
                  >
                    <X size={14} />
                  </button>
                </div>
              ))}
            </div>
            {selection.size === 0 && (
              <p className="modal-intro">
                Nenhum jogo selecionado. Volte à biblioteca para escolher.
              </p>
            )}
            {state.playlist.length + selection.size > 500 && (
              <p className="modal-error" role="alert">
                A lista pode ter até 500 jogos. Reduza a seleção antes de
                adicionar.
              </p>
            )}
            <div className="modal-actions">
              <button
                className="button secondary"
                onClick={() => setModal(null)}
              >
                Continuar escolhendo
              </button>
              <button
                className="button primary"
                disabled={
                  editingLocked ||
                  !selection.size ||
                  !validMinutes(minutes) ||
                  state.playlist.length + selection.size > 500
                }
                onClick={() => void addSelection()}
              >
                <Plus size={14} />
                Adicionar à sequência
              </button>
            </div>
          </Modal>
        )}
        {(modal === "save" || modal === "rename") && (
          <Modal
            key={modal}
            error={dialogError}
            title={modal === "save" ? "Salvar sequência" : "Renomear sequência"}
            onClose={() => setModal(null)}
          >
            <p className="modal-intro">
              {modal === "save"
                ? "Guarde os jogos, a ordem e os tempos para usar novamente."
                : "Escolha um novo nome para esta sequência."}
            </p>
            <form
              className="sequence-form"
              onSubmit={async (event) => {
                event.preventDefault();
                const result = await act(
                  modal === "save" ? "savePlaylist" : "renamePlaylist",
                  { name: savedName, savedId: savedSelection?.id },
                  modal === "save"
                    ? "Sequência salva neste computador."
                    : "Nome atualizado.",
                );
                if (result) setModal(modal === "save" ? null : "saved");
              }}
            >
              <label className="field-label" htmlFor="saved-name">
                Nome da sequência
              </label>
              <input
                id="saved-name"
                data-autofocus
                required
                maxLength={60}
                placeholder="Ex.: Minha seleção da noite"
                value={savedName}
                onChange={(event) => setSavedName(event.target.value)}
                autoComplete="off"
              />
              <div className="modal-actions">
                <button
                  type="button"
                  className="button secondary"
                  onClick={() => setModal(modal === "save" ? null : "saved")}
                >
                  Cancelar
                </button>
                <button
                  type="submit"
                  className="button primary"
                  disabled={editingLocked || !savedName.trim()}
                >
                  <Save size={14} />
                  Salvar
                </button>
              </div>
            </form>
          </Modal>
        )}
        {modal === "saved" && (
          <Modal
            key="saved"
            error={dialogError}
            title="Sequências salvas"
            onClose={() => setModal(null)}
          >
            <p className="modal-intro">
              Suas seleções guardadas neste computador.
            </p>
            {state.savedPlaylists.length === 0 ? (
              <div className="saved-empty">
                <FolderOpen size={27} />
                <h3>Suas sequências ficam aqui.</h3>
                <p>
                  Monte uma lista e use o botão de salvar para reutilizá-la
                  depois.
                </p>
              </div>
            ) : (
              <div className="saved-library" data-testid="saved-library">
                {state.savedPlaylists.map((saved) => (
                  <div
                    className="saved-sequence"
                    key={saved.id}
                    data-saved-id={saved.id}
                  >
                    <div className="saved-covers" aria-hidden="true">
                      {saved.games.map((game, index) => (
                        <div key={index}>
                          <span>{game.name.slice(0, 1)}</span>
                          {game.iconUrl && (
                            <img
                              src={game.iconUrl}
                              alt=""
                              onError={(event) =>
                                (event.currentTarget.style.visibility =
                                  "hidden")
                              }
                            />
                          )}
                        </div>
                      ))}
                    </div>
                    <div className="saved-copy">
                      <h3 title={saved.name}>{saved.name}</h3>
                      <p>
                        {saved.count} {saved.count === 1 ? "jogo" : "jogos"} ·{" "}
                        {duration(saved.totalMinutes)}
                      </p>
                    </div>
                    <div className="saved-actions">
                      <button
                        className="icon-button"
                        aria-label={`Renomear ${saved.name}`}
                        title="Renomear"
                        disabled={editingLocked}
                        onClick={() => {
                          setSavedSelection(saved);
                          setSavedName(saved.name);
                          setModal("rename");
                        }}
                      >
                        <Pencil size={13} />
                      </button>
                      <button
                        className="icon-button remove-row"
                        aria-label={`Excluir sequência salva ${saved.name}`}
                        title="Excluir sequência salva"
                        disabled={editingLocked}
                        onClick={() => {
                          setSavedSelection(saved);
                          setModal("deleteSaved");
                        }}
                      >
                        <X size={15} />
                      </button>
                      <button
                        className="button secondary small"
                        aria-label={`Usar sequência ${saved.name}`}
                        disabled={editingLocked}
                        onClick={async () => {
                          setSavedSelection(saved);
                          if (state.playlist.length) setModal("loadSaved");
                          else {
                            const result = await act("usePlaylist", {
                              savedId: saved.id,
                              merge: "replace",
                            });
                            if (result) {
                              navigate("playlist");
                              setModal(null);
                            }
                          }
                        }}
                      >
                        Usar
                        <ArrowRight size={12} />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
            {queueRunning && (
              <p className="help-footnote">
                Encerre a sequência atual para carregar ou editar uma seleção
                salva.
              </p>
            )}
          </Modal>
        )}
        {modal === "loadSaved" && savedSelection && (
          <Modal
            key="loadSaved"
            error={dialogError}
            title={`Carregar “${savedSelection.name}”`}
            onClose={() => setModal("saved")}
          >
            <p className="modal-intro">
              Sua lista atual tem {state.playlist.length}{" "}
              {state.playlist.length === 1 ? "jogo" : "jogos"}. Como deseja usar
              os {savedSelection.count} jogos da sequência salva?
            </p>
            <div className="load-options">
              {(["append", "replace"] as const).map((merge) => (
                <button
                  key={merge}
                  className="load-option"
                  disabled={editingLocked}
                  onClick={async () => {
                    const result = await act("usePlaylist", {
                      savedId: savedSelection.id,
                      merge,
                    });
                    if (result) {
                      navigate("playlist");
                      setModal(null);
                    }
                  }}
                >
                  <span className="load-option-icon">
                    {merge === "append" ? (
                      <Plus size={18} />
                    ) : (
                      <RefreshCw size={18} />
                    )}
                  </span>
                  <span>
                    <strong>
                      {merge === "append"
                        ? "Acrescentar ao final"
                        : "Substituir a lista atual"}
                    </strong>
                    <small>
                      {merge === "append"
                        ? "Mantém os jogos que já estão na lista."
                        : "Carrega somente a sequência salva. Você pode desfazer."}
                    </small>
                  </span>
                  <ChevronRight size={15} />
                </button>
              ))}
            </div>
          </Modal>
        )}
        {modal === "deleteSaved" && savedSelection && (
          <Modal
            key="deleteSaved"
            error={dialogError}
            title="Excluir sequência salva?"
            onClose={() => setModal("saved")}
          >
            <p className="modal-intro">
              “{savedSelection.name}” será excluída das suas seleções salvas. Os
              jogos da lista atual serão mantidos.
            </p>
            <div className="modal-actions">
              <button
                className="button secondary"
                onClick={() => setModal("saved")}
              >
                Manter
              </button>
              <button
                className="button primary"
                disabled={editingLocked}
                onClick={async () => {
                  const result = await act(
                    "deletePlaylist",
                    { savedId: savedSelection.id },
                    "Sequência salva excluída.",
                  );
                  if (result) setModal("saved");
                }}
              >
                Excluir seleção
              </button>
            </div>
          </Modal>
        )}
        {modal === "durationAll" && (
          <Modal
            key="durationAll"
            error={dialogError}
            title="Ajustar todos os tempos"
            onClose={() => setModal(null)}
          >
            <p className="modal-intro">
              Defina a duração de cada um dos {state.playlist.length} jogos da
              lista atual. As sequências salvas são mantidas.
            </p>
            <form
              className="sequence-form"
              onSubmit={async (event) => {
                event.preventDefault();
                const result = await act("durationAll", {
                  minutes: Number(allMinutes),
                });
                if (result) setModal(null);
              }}
            >
              <label className="field-label" htmlFor="all-minutes">
                Minutos por jogo
              </label>
              <input
                id="all-minutes"
                data-autofocus
                type="number"
                min={1}
                max={1440}
                step={1}
                required
                value={allMinutes}
                onChange={(event) => setAllMinutes(event.target.value)}
              />
              <div className="duration-presets">
                {[16, 30, 60].map((value) => (
                  <button
                    type="button"
                    key={value}
                    className={Number(allMinutes) === value ? "active" : ""}
                    onClick={() => setAllMinutes(String(value))}
                  >
                    {value} min
                  </button>
                ))}
              </div>
              <p className="batch-total">
                Duração total:{" "}
                <strong>
                  {validMinutes(allMinutes)
                    ? duration(Number(allMinutes) * state.playlist.length)
                    : "—"}
                </strong>
              </p>
              <div className="modal-actions">
                <button
                  type="button"
                  className="button secondary"
                  onClick={() => setModal(null)}
                >
                  Cancelar
                </button>
                <button
                  type="submit"
                  className="button primary"
                  disabled={editingLocked || !validMinutes(allMinutes)}
                >
                  Aplicar aos {state.playlist.length} jogos
                </button>
              </div>
            </form>
          </Modal>
        )}
        {modal === "help" && (
          <Modal
            key="help"
            error={dialogError}
            title="Seu espaço. Seu ritmo."
            onClose={() => setModal(null)}
          >
            <p className="modal-intro">
              Uma atividade de jogo em poucos passos.
            </p>
            <ol className="help-steps">
              <li>
                <span>01</span>
                <div>
                  <strong>Encontre um jogo</strong>
                  <p>Busque pelo nome e selecione o card.</p>
                </div>
              </li>
              <li>
                <span>02</span>
                <div>
                  <strong>Escolha como aparecer</strong>
                  <p>
                    O Mirage inicia um processo reconhecível para o jogo escolhido,
                    individualmente ou em uma sequência.
                  </p>
                </div>
              </li>
              <li>
                <span>03</span>
                <div>
                  <strong>Defina seu tempo</strong>
                  <p>
                    Adicione jogos pelo menu ⋮, organize a lista e inicie quando
                    estiver pronto.
                  </p>
                </div>
              </li>
            </ol>
            <div className="help-note">
              <kbd>Ctrl K</kbd> vai direto para a busca. <kbd>Esc</kbd> fecha
              menus. <kbd>Ctrl Z</kbd> desfaz a última alteração na sequência.
            </div>
            <p className="help-footnote">
              Mantenha o Discord para Windows aberto. O reconhecimento da
              atividade é controlado pelo Discord.
            </p>
            <button
              className="button primary w-full"
              onClick={() => setModal(null)}
            >
              Entendi
              <Check size={15} />
            </button>
          </Modal>
        )}
        {modal === "clear" && (
          <Modal
            key="clear"
            error={dialogError}
            title="Limpar sua sequência?"
            onClose={() => setModal(null)}
          >
            <p className="modal-intro">
              Os {state.playlist.length} jogos serão removidos desta lista. Você
              pode desfazer essa alteração enquanto o app estiver aberto.
            </p>
            <div className="modal-actions">
              <button
                className="button secondary"
                onClick={() => setModal(null)}
              >
                Manter lista
              </button>
              <button
                className="button primary"
                onClick={() => {
                  void act("clear", {}, "Sequência limpa.");
                  setModal(null);
                }}
              >
                Limpar sequência
              </button>
            </div>
          </Modal>
        )}
      </AnimatePresence>
    </div>
  );
}
