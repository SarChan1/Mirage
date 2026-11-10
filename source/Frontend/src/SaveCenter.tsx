import { useEffect, useMemo, useState } from "react";
import { Archive, ArrowRight, CalendarClock, FolderArchive, FolderOpen, HardDrive, LoaderCircle, RefreshCw, Search } from "lucide-react";
import { request } from "./bridge";
import { useI18n } from "./i18n";
import "./save-center.css";

type InstalledSaveGame = { gameId: string; name: string; initial: string; iconUrl: string | null; heroUrl: string | null; installFolder: string | null; locationCount: number; fileCount: number; sizeBytes: number; lastModified: string | null; installed: boolean };
type SaveLibraryResult = { games: InstalledSaveGame[]; installedCount: number; withSavesCount: number; orphanCount: number; fileCount: number; sizeBytes: number; scannedAt: string; limited: boolean };
type ExportResult = { selected: boolean; path: string | null; games: number; files: number; sizeBytes: number; format: "zip" | "folder" };

const bytes = (value: number, locale: string) => {
  if (value < 1024) return `${value} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let size = value / 1024, index = 0;
  while (size >= 1024 && index < units.length - 1) { size /= 1024; index++; }
  return `${size.toLocaleString(locale, { maximumFractionDigits: size < 10 ? 1 : 0 })} ${units[index]}`;
};

export function SaveCenter({ preview, onInspect }: { preview: boolean; onInspect: (gameId: string) => Promise<void> }) {
  const { locale } = useI18n();
  const [data, setData] = useState<SaveLibraryResult | null>(null);
  const [query, setQuery] = useState("");
  const [sort, setSort] = useState<"recent" | "name" | "size">("recent");
  const [busy, setBusy] = useState(false);
  const [exporting, setExporting] = useState<"zip" | "folder" | null>(null);
  const [error, setError] = useState("");
  const [exported, setExported] = useState<ExportResult | null>(null);
  async function scan() {
    setBusy(true); setError("");
    try { setData(await request<SaveLibraryResult>("saves.scanInstalled")); }
    catch (reason) { setError((reason as Error).message); }
    finally { setBusy(false); }
  }
  useEffect(() => { void scan(); }, []);
  const games = useMemo(() => {
    const value = (data?.games ?? []).filter(game => game.name.toLocaleLowerCase(locale).includes(query.trim().toLocaleLowerCase(locale)));
    return value.sort((a, b) => sort === "name" ? a.name.localeCompare(b.name, locale) : sort === "size" ? b.sizeBytes - a.sizeBytes : (Date.parse(b.lastModified ?? "") || 0) - (Date.parse(a.lastModified ?? "") || 0));
  }, [data, query, sort, locale]);
  const installedGames = games.filter(game => game.installed);
  const orphanGames = games.filter(game => !game.installed);
  const card = (game: InstalledSaveGame) => <article className={`installed-save-card ${game.installed ? "" : "is-orphan"}`} key={game.gameId}>
    <div className="installed-save-art">{game.iconUrl ? <img src={game.iconUrl} alt="" /> : <span>{game.initial}</span>}</div>
    <div className="installed-save-main"><div className="installed-title"><strong>{game.name}</strong>{!game.installed && <span>Só os saves</span>}</div><span className="installed-path" title={game.installFolder ?? ""}>{game.installFolder ?? "Jogo não instalado · saves preservados no computador"}</span><div className="installed-meta"><span><FolderOpen size={12} /> {game.locationCount ? `${game.locationCount} locais` : "Nenhum save localizado"}</span><span><Archive size={12} /> {game.fileCount.toLocaleString(locale)} arquivos</span><span>{bytes(game.sizeBytes, locale)}</span>{game.lastModified && <span><CalendarClock size={12} /> Alterado em {new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" }).format(new Date(game.lastModified))}</span>}</div></div>
    <button className="button secondary small" onClick={() => void onInspect(game.gameId)}>Ver saves <ArrowRight size={13} /></button>
  </article>;
  async function backup(format: "zip" | "folder") {
    setExporting(format); setError(""); setExported(null);
    try { const result = await request<ExportResult>("saves.export", { format }); if (result.selected) setExported(result); }
    catch (reason) { setError((reason as Error).message); }
    finally { setExporting(null); }
  }
  return <section className="save-center" data-testid="save-center">
    <div className="save-center-actions">
      <div className="save-center-search"><Search size={16} /><input aria-label="Buscar jogos instalados" placeholder="Buscar nos jogos instalados…" value={query} onChange={event => setQuery(event.target.value)} /></div>
      <select aria-label="Ordenar central de saves" value={sort} onChange={event => setSort(event.target.value as typeof sort)}><option value="recent">Alterados recentemente</option><option value="name">Nome do jogo</option><option value="size">Maior tamanho</option></select>
      <button className="button ghost small" disabled={busy || Boolean(exporting)} onClick={() => void scan()}><RefreshCw size={14} className={busy ? "spin" : ""} /> Atualizar</button>
    </div>
    {error && <p className="save-center-error" role="alert">{error}</p>}
    {busy && !data ? <div className="save-center-loading"><LoaderCircle className="spin" size={24} /><strong>Procurando jogos e saves…</strong><span>Apenas locais conhecidos deste computador são verificados.</span></div> : data && <>
      <div className="save-overview">
        <div><HardDrive size={17} /><span><strong>{data.installedCount}</strong><small>jogos instalados</small></span></div>
        <div><FolderOpen size={17} /><span><strong>{data.orphanCount}</strong><small>saves sem jogo instalado</small></span></div>
        <div><Archive size={17} /><span><strong>{data.fileCount.toLocaleString(locale)}</strong><small>arquivos mapeados</small></span></div>
        <div><FolderArchive size={17} /><span><strong>{bytes(data.sizeBytes, locale)}</strong><small>tamanho estimado</small></span></div>
      </div>
      <div className="save-backup-card">
          <div><span className="save-backup-icon"><FolderArchive size={19} /></span><span><strong>Backup organizado</strong><small>Copie saves instalados e órfãos sem alterar os arquivos originais.</small></span></div>
        <div><button className="button secondary small" disabled={preview || !data.withSavesCount || Boolean(exporting)} onClick={() => void backup("folder")}>{exporting === "folder" ? <LoaderCircle className="spin" size={14} /> : <FolderOpen size={14} />} Exportar pasta</button><button className="button primary small" disabled={preview || !data.withSavesCount || Boolean(exporting)} onClick={() => void backup("zip")}>{exporting === "zip" ? <LoaderCircle className="spin" size={14} /> : <Archive size={14} />} Criar ZIP</button></div>
      </div>
      {preview && <p className="save-center-note">A prévia visual não cria arquivos de backup.</p>}
      {exported?.path && <div className="save-export-result" role="status"><div><strong>Backup concluído</strong><span>{exported.games} jogos · {exported.files.toLocaleString(locale)} arquivos · {bytes(exported.sizeBytes, locale)}</span><code>{exported.path}</code></div><button className="button secondary small" onClick={() => void request("saves.openExport")}><FolderOpen size={14} /> Abrir local</button></div>}
      {data.limited && <p className="save-center-note">A verificação foi limitada para manter o aplicativo responsivo. Atualize para tentar novamente.</p>}
      <div className="installed-save-list">
        {installedGames.length > 0 && <><div className="save-group-title"><span>Jogos instalados</span><small>{installedGames.length}</small></div>{installedGames.map(card)}</>}
        {orphanGames.length > 0 && <><div className="save-group-title orphan"><span>Saves órfãos</span><small>{orphanGames.length}</small></div><p className="orphan-explanation">O jogo não foi detectado, mas os arquivos de save continuam no computador.</p>{orphanGames.map(card)}</>}
        {!games.length && <div className="save-center-empty"><HardDrive size={25} /><strong>{query ? "Nenhum jogo instalado corresponde à busca." : "Nenhum jogo instalado foi detectado."}</strong><span>Steam, Epic e instalações registradas no Windows são verificadas.</span></div>}
      </div>
    </>}
  </section>;
}
