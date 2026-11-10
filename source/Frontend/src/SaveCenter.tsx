import { useEffect, useMemo, useState } from "react";
import { Activity, AlertTriangle, Archive, ArrowRight, CalendarClock, CheckCircle2, Clock3, Download, FolderArchive, FolderOpen, HardDrive, LoaderCircle, MoveRight, RefreshCw, Search, ShieldCheck, Trash2, UploadCloud } from "lucide-react";
import { request } from "./bridge";
import { useI18n } from "./i18n";
import "./save-center.css";

type InstalledSaveGame = { gameId: string; name: string; initial: string; iconUrl: string | null; heroUrl: string | null; installFolder: string | null; locationCount: number; fileCount: number; sizeBytes: number; lastModified: string | null; installed: boolean; locations?: string[] };
type SaveChange = { id: string; gameId: string; gameName: string; type: "added" | "removed" | "moved" | "modified" | "installed" | "uninstalled"; detectedAt: string; previousPath?: string | null; currentPath?: string | null };
type SaveLibraryResult = { games: InstalledSaveGame[]; installedCount: number; withSavesCount: number; orphanCount: number; fileCount: number; sizeBytes: number; scannedAt: string; limited: boolean; changes?: SaveChange[]; fromCache?: boolean };
type ExportResult = { selected: boolean; path: string | null; games: number; files: number; sizeBytes: number; format: "zip" | "folder" };
type RestoreGame = { gameId: string; name: string; status: "ready" | "conflict" | "pending" | "invalid"; target: string | null; installed: boolean; files: number; sizeBytes: number; conflicts: number; note: string };
type RestoreAnalysis = { token: string; sourceName: string; createdAt: string | null; games: RestoreGame[]; files: number; sizeBytes: number; readyGames: number; pendingGames: number; conflicts: number; legacy: boolean; selected?: boolean };
type RestoreResult = { restoredFiles: number; preservedConflicts: number; pendingFiles: number; rollbackPath: string | null; pendingPath: string | null; analysis: RestoreAnalysis };
type Busy = "loading" | "checking" | "full" | null;

let sessionData: SaveLibraryResult | null = null;
let sessionValidated = false;
let sessionRequest: Promise<SaveLibraryResult> | null = null;
const previewRestore: RestoreAnalysis = { token: "preview", sourceName: "Mirage-Saves-2026-11-10.zip", createdAt: "2026-11-10T12:00:00Z", files: 47, sizeBytes: 12_800_000, readyGames: 2, pendingGames: 1, conflicts: 3, legacy: false, games: [
  { gameId: "preview-cyberpunk", name: "Cyberpunk 2077", status: "conflict", target: String.raw`C:\Users\Player\Saved Games\CD Projekt Red\Cyberpunk 2077`, installed: true, files: 18, sizeBytes: 6_300_000, conflicts: 3, note: "Já existem saves nesse destino. Você escolherá como tratar os conflitos." },
  { gameId: "preview-stardew", name: "Stardew Valley", status: "ready", target: String.raw`C:\Users\Player\AppData\Roaming\StardewValley\Saves`, installed: true, files: 21, sizeBytes: 4_900_000, conflicts: 0, note: "Destino atual reconhecido e pronto para restauração." },
  { gameId: "preview-witcher", name: "The Witcher 3: Wild Hunt", status: "pending", target: null, installed: false, files: 8, sizeBytes: 1_600_000, conflicts: 0, note: "O jogo não está instalado. Os arquivos serão guardados até você instalá-lo e criar um save." },
] };

const bytes = (value: number, locale: string) => {
  if (value < 1024) return `${value} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let size = value / 1024, index = 0;
  while (size >= 1024 && index < units.length - 1) { size /= 1024; index++; }
  return `${size.toLocaleString(locale, { maximumFractionDigits: size < 10 ? 1 : 0 })} ${units[index]}`;
};

function run(command: "saves.scanInstalled" | "saves.validate") {
  if (!sessionRequest) sessionRequest = request<SaveLibraryResult>(command).then(result => (sessionData = result)).finally(() => { sessionRequest = null; });
  return sessionRequest;
}

export function SaveCenter({ preview, onInspect }: { preview: boolean; onInspect: (gameId: string) => Promise<void> }) {
  const { locale } = useI18n();
  const [data, setData] = useState<SaveLibraryResult | null>(sessionData);
  const [query, setQuery] = useState("");
  const [sort, setSort] = useState<"recent" | "name" | "size">("recent");
  const [busy, setBusy] = useState<Busy>(sessionData ? null : "loading");
  const [exporting, setExporting] = useState<"zip" | "folder" | null>(null);
  const [error, setError] = useState("");
  const [exported, setExported] = useState<ExportResult | null>(null);
  const [restore, setRestore] = useState<RestoreAnalysis | null>(preview ? previewRestore : null);
  const [restored, setRestored] = useState<RestoreResult | null>(null);
  const [restoreMode, setRestoreMode] = useState<"merge" | "replace">("merge");
  const [restoreBusy, setRestoreBusy] = useState(false);

  useEffect(() => {
    let active = true;
    async function open() {
      if (sessionData) {
        if (active) setData(sessionData);
      } else {
        setBusy("loading");
        try {
          const cached = await request<SaveLibraryResult & { empty?: boolean }>("saves.load");
          if (!cached.empty) { sessionData = cached; if (active) setData(cached); }
          else { const first = await run("saves.scanInstalled"); if (active) setData(first); sessionValidated = true; }
        } catch (reason) { if (active) setError((reason as Error).message); }
        finally { if (active) setBusy(null); }
      }
      if (!sessionValidated && sessionData) {
        sessionValidated = true;
        if (active) setBusy("checking");
        try { const checked = await run("saves.validate"); if (active) setData(checked); }
        catch (reason) { if (active) setError((reason as Error).message); }
        finally { if (active) setBusy(null); }
      }
    }
    void open();
    return () => { active = false; };
  }, []);

  async function scan() {
    setBusy("full"); setError("");
    try { setData(await run("saves.scanInstalled")); sessionValidated = true; }
    catch (reason) { setError((reason as Error).message); }
    finally { setBusy(null); }
  }

  const games = useMemo(() => {
    const value = (data?.games ?? []).filter(game => game.name.toLocaleLowerCase(locale).includes(query.trim().toLocaleLowerCase(locale)));
    return value.sort((a, b) => sort === "name" ? a.name.localeCompare(b.name, locale) : sort === "size" ? b.sizeBytes - a.sizeBytes : (Date.parse(b.lastModified ?? "") || 0) - (Date.parse(a.lastModified ?? "") || 0));
  }, [data, query, sort, locale]);
  const installedGames = games.filter(game => game.installed);
  const orphanGames = games.filter(game => !game.installed);
  const formatDate = (value: string, detailed = true) => new Intl.DateTimeFormat(locale, detailed ? { dateStyle: "medium", timeStyle: "short" } : { dateStyle: "medium" }).format(new Date(value));
  const card = (game: InstalledSaveGame) => <article className={`installed-save-card ${game.installed ? "" : "is-orphan"}`} key={game.gameId}>
    <div className="installed-save-art">{game.iconUrl ? <img src={game.iconUrl} alt="" /> : <span>{game.initial}</span>}</div>
    <div className="installed-save-main"><div className="installed-title"><strong>{game.name}</strong>{!game.installed && <span>Só os saves</span>}</div><span className="installed-path" title={game.installFolder ?? ""}>{game.installFolder ?? "Jogo não instalado · saves preservados no computador"}</span><div className="installed-meta"><span><FolderOpen size={12} /> {game.locationCount ? `${game.locationCount} locais` : "Nenhum save localizado"}</span><span><Archive size={12} /> {game.fileCount.toLocaleString(locale)} arquivos</span><span>{bytes(game.sizeBytes, locale)}</span>{game.lastModified && <span><CalendarClock size={12} /> Alterado em {formatDate(game.lastModified)}</span>}</div></div>
    <button className="button secondary small" onClick={() => void onInspect(game.gameId)}>Ver saves <ArrowRight size={13} /></button>
  </article>;

  async function backup(format: "zip" | "folder") {
    setExporting(format); setError(""); setExported(null);
    try { const result = await request<ExportResult>("saves.export", { format }); if (result.selected) setExported(result); }
    catch (reason) { setError((reason as Error).message); }
    finally { setExporting(null); }
  }

  async function chooseRestore(command: "saves.restore.chooseZip" | "saves.restore.chooseFolder") {
    setRestoreBusy(true); setError(""); setRestored(null);
    try { const result = await request<RestoreAnalysis>(command); if (result.token) setRestore(result); }
    catch (reason) { setError((reason as Error).message); }
    finally { setRestoreBusy(false); }
  }
  async function applyRestore() {
    if (!restore) return;
    setRestoreBusy(true); setError("");
    try { const result = await request<RestoreResult>("saves.restore.apply", { token: restore.token, mode: restoreMode }); setRestored(result); setData(await run("saves.validate")); }
    catch (reason) { setError((reason as Error).message); }
    finally { setRestoreBusy(false); }
  }

  const changes = data?.changes ?? [];
  const changeCopy = (change: SaveChange) => ({ added: "Novo local de save encontrado", removed: "Local de save removido", moved: "Save movido para outro local", modified: "Arquivos de save alterados", installed: "Jogo instalado novamente", uninstalled: "Jogo desinstalado · saves preservados" }[change.type]);
  const changeIcon = (change: SaveChange) => change.type === "moved" ? <MoveRight size={14} /> : change.type === "removed" ? <Trash2 size={14} /> : change.type === "modified" ? <Activity size={14} /> : <CheckCircle2 size={14} />;

  return <section className="save-center" data-testid="save-center">
    <div className="save-center-actions">
      <div className="save-center-search"><Search size={16} /><input aria-label="Buscar jogos e saves" placeholder="Buscar jogos e saves…" value={query} onChange={event => setQuery(event.target.value)} /></div>
      <select aria-label="Ordenar central de saves" value={sort} onChange={event => setSort(event.target.value as typeof sort)}><option value="recent">Alterados recentemente</option><option value="name">Nome do jogo</option><option value="size">Maior tamanho</option></select>
      <button className="button secondary small save-refresh" disabled={Boolean(busy) || Boolean(exporting)} onClick={() => void scan()}><RefreshCw size={14} className={busy ? "spin" : ""} /> Verificar agora</button>
    </div>
    {data && <div className="save-sync-line"><span className={busy === "checking" ? "is-checking" : ""}>{busy === "checking" ? <><LoaderCircle size={12} className="spin" /> Conferindo mudanças sem refazer toda a busca…</> : <><ShieldCheck size={12} /> Biblioteca salva neste computador</>}</span><span><Clock3 size={12} /> Última verificação: {formatDate(data.scannedAt)}</span></div>}
    {error && <p className="save-center-error" role="alert">{error}</p>}
    {!data ? <div className="save-center-loading"><div className="save-loading-mark"><LoaderCircle className="spin" size={22} /></div><strong>Preparando sua biblioteca de saves</strong><span>Na primeira vez, o Mirage mapeia somente locais conhecidos. Depois, o resultado fica salvo.</span><div className="save-skeleton"><i /><i /><i /></div></div> : <>
      <div className="save-overview">
        <div><HardDrive size={17} /><span><strong>{data.installedCount}</strong><small>jogos instalados</small></span></div>
        <div><FolderOpen size={17} /><span><strong>{data.orphanCount}</strong><small>saves sem jogo instalado</small></span></div>
        <div><Archive size={17} /><span><strong>{data.fileCount.toLocaleString(locale)}</strong><small>arquivos mapeados</small></span></div>
        <div><FolderArchive size={17} /><span><strong>{bytes(data.sizeBytes, locale)}</strong><small>tamanho estimado</small></span></div>
      </div>
      <div className="save-feature-row">
        <div className="save-backup-card"><div><span className="save-backup-icon"><FolderArchive size={19} /></span><span><strong>Backup organizado</strong><small>Copie saves instalados e órfãos sem alterar os arquivos originais.</small></span></div><div><button className="button secondary small" disabled={preview || !data.withSavesCount || Boolean(exporting)} onClick={() => void backup("folder")}>{exporting === "folder" ? <LoaderCircle className="spin" size={14} /> : <FolderOpen size={14} />} Exportar pasta</button><button className="button primary small" disabled={preview || !data.withSavesCount || Boolean(exporting)} onClick={() => void backup("zip")}>{exporting === "zip" ? <LoaderCircle className="spin" size={14} /> : <Archive size={14} />} Criar ZIP</button></div></div>
        <div className="save-health-card"><ShieldCheck size={18} /><span><strong>Originais protegidos</strong><small>A Central apenas lê metadados. Nada é movido ou excluído.</small></span></div>
      </div>
      <div className="save-restore-card">
        <div className="save-restore-intro"><span className="save-restore-icon"><UploadCloud size={18} /></span><span><strong>Reinserir saves de um backup</strong><small>Analise um ZIP ou uma pasta antes de devolver os arquivos aos jogos.</small></span><div><button className="button secondary small" disabled={preview || restoreBusy} onClick={() => void chooseRestore("saves.restore.chooseFolder")}><FolderOpen size={14} /> Pasta</button><button className="button secondary small" disabled={preview || restoreBusy} onClick={() => void chooseRestore("saves.restore.chooseZip")}>{restoreBusy ? <LoaderCircle className="spin" size={14} /> : <Download size={14} />} Selecionar ZIP</button></div></div>
        <div className="save-restore-warning"><AlertTriangle size={14} /><span><strong>Depois de formatar ou reinstalar um jogo, abra-o e crie um save uma vez.</strong><small>Alguns jogos geram uma nova pasta, um novo perfil ou outro identificador. O Mirage prioriza a pasta atual e avisa quando só encontra o caminho antigo.</small></span></div>
        {restore && <div className="save-restore-analysis">
          <div className="restore-summary"><span><strong>{restore.sourceName}</strong><small>{restore.games.length} jogos · {restore.files.toLocaleString(locale)} arquivos · {bytes(restore.sizeBytes, locale)}</small></span><span className="restore-summary-badges"><i className="ready">{restore.readyGames} prontos</i>{restore.pendingGames > 0 && <i className="pending">{restore.pendingGames} aguardando</i>}{restore.conflicts > 0 && <i className="conflict">{restore.conflicts} conflitos</i>}</span></div>
          {restore.legacy && <p className="restore-legacy"><AlertTriangle size={13} /> Este é um backup antigo. Caminhos atuais serão usados quando reconhecidos; o restante ficará guardado para revisão.</p>}
          <div className="restore-game-list">{restore.games.slice(0, 8).map(game => <div className={`restore-game is-${game.status}`} key={game.gameId}><span className="restore-state-dot" /><span><strong>{game.name}</strong><small>{game.note}</small>{game.target && <code title={game.target}>{game.target}</code>}</span><span>{game.files} arquivos{game.conflicts > 0 && <small>{game.conflicts} conflitos</small>}</span></div>)}</div>
          <div className="restore-policy"><label><input type="radio" name="restore-mode" checked={restoreMode === "merge"} onChange={() => setRestoreMode("merge")} /><span><strong>Mesclar com segurança</strong><small>Mantém saves atuais e separa arquivos conflitantes para revisão.</small></span></label><label><input type="radio" name="restore-mode" checked={restoreMode === "replace"} onChange={() => setRestoreMode("replace")} /><span><strong>Substituir com ponto de restauração</strong><small>Guarda os saves atuais em um ZIP antes de substituí-los.</small></span></label><button className="button primary small" disabled={preview || restoreBusy || !restore.files} onClick={() => void applyRestore()}>{restoreBusy ? <LoaderCircle className="spin" size={14} /> : <UploadCloud size={14} />} Restaurar saves</button></div>
        </div>}
        {restored && <div className="restore-result" role="status"><CheckCircle2 size={16} /><span><strong>Restauração concluída</strong><small>{restored.restoredFiles} inseridos · {restored.preservedConflicts} conflitos preservados · {restored.pendingFiles} aguardando destino</small></span><div>{restored.pendingPath && <button className="button ghost small" onClick={() => void request("saves.restore.open", { kind: "pending" })}>Abrir pendentes</button>}{restored.rollbackPath && <button className="button ghost small" onClick={() => void request("saves.restore.open", { kind: "rollback" })}>Abrir ponto de restauração</button>}</div></div>}
      </div>
      {changes.length > 0 && <div className="save-activity"><div className="save-activity-heading"><span><Activity size={14} /><strong>Atividade recente</strong></span><small>Últimas mudanças detectadas</small></div><div className="save-change-list">{changes.slice(0, 4).map(change => <div className={`save-change is-${change.type}`} key={change.id}><span className="save-change-icon">{changeIcon(change)}</span><span><strong>{change.gameName}</strong><small>{changeCopy(change)}</small></span><time>{formatDate(change.detectedAt, false)}</time></div>)}</div></div>}
      {preview && <p className="save-center-note">A prévia visual não cria arquivos de backup.</p>}
      {exported?.path && <div className="save-export-result" role="status"><div><strong>Backup concluído</strong><span>{exported.games} jogos · {exported.files.toLocaleString(locale)} arquivos · {bytes(exported.sizeBytes, locale)}</span><code>{exported.path}</code></div><button className="button secondary small" onClick={() => void request("saves.openExport")}><FolderOpen size={14} /> Abrir local</button></div>}
      {data.limited && <p className="save-center-note">A verificação foi limitada para manter o aplicativo responsivo. Atualize para tentar novamente.</p>}
      <div className="installed-save-list">
        {installedGames.length > 0 && <><div className="save-group-title"><span>Jogos instalados</span><small>{installedGames.length}</small></div>{installedGames.map(card)}</>}
        {orphanGames.length > 0 && <><div className="save-group-title orphan"><span>Saves órfãos</span><small>{orphanGames.length}</small></div><p className="orphan-explanation">O jogo não foi detectado, mas os arquivos de save continuam no computador.</p>{orphanGames.map(card)}</>}
        {!games.length && <div className="save-center-empty"><HardDrive size={25} /><strong>{query ? "Nenhum jogo ou save corresponde à busca." : "Nenhum jogo ou save foi detectado."}</strong><span>Use “Verificar agora” depois de criar um save ou instalar um jogo.</span></div>}
      </div>
    </>}
  </section>;
}
