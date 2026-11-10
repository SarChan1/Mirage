import { useEffect, useRef, useState } from "react";
import { Check, Copy, FolderOpen, HardDrive, LoaderCircle, RefreshCw, Search } from "lucide-react";
import { request } from "./bridge";
import type { Game } from "./types";
import "./saves.css";
import "./preferences.css";
import { useI18n } from "./i18n";

export type SaveSearchResult = {
  gameName: string;
  catalogName: string | null;
  catalogCount: number;
  catalogUpdatedAt: string;
  installDetected: boolean;
  installFolder: string | null;
  locations: { id: string; path: string; kind: "folder" | "file" | "registry"; basis: "catalog" | "candidate"; lastModified: string | null; sizeBytes: number; fileCount: number }[];
  rulesChecked: number;
  rulesSkipped: number;
  limited: boolean;
};

export function SaveFinderPanel({ game }: { game: Game }) {
  const { locale } = useI18n();
  const [result, setResult] = useState<SaveSearchResult | null>(null);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState("");
  const [manual, setManual] = useState(false);
  const [copied, setCopied] = useState("");
  const [copying, setCopying] = useState(false);
  const revision = useRef(0);
  useEffect(() => { if (copied) { const timer = setTimeout(() => setCopied(""), 1800); return () => clearTimeout(timer); } }, [copied]);
  useEffect(() => {
    const token = ++revision.current;
    setBusy(true); setError(""); setResult(null); setManual(false);
    request<SaveSearchResult>("saves.find", { gameId: game.id })
      .then(data => { if (token === revision.current) setResult(data); })
      .catch(reason => { if (token === revision.current) setError((reason as Error).message); })
      .finally(() => { if (token === revision.current) setBusy(false); });
    return () => { revision.current++; };
  }, [game.id]);
  async function scan(useManual: boolean, choose = false) {
    const token = ++revision.current;
    setBusy(true); setError("");
    try {
      if (choose) {
        const picked = await request<{ selected: boolean }>("saves.chooseFolder", { gameId: game.id });
        if (!picked.selected) return;
      }
      const data = await request<SaveSearchResult>("saves.find", { gameId: game.id, manual: useManual });
      if (token === revision.current) { setResult(data); setManual(useManual); }
    } catch (reason) { if (token === revision.current) setError((reason as Error).message); }
    finally { if (token === revision.current) setBusy(false); }
  }
  async function open(id: string) {
    try { await request("saves.open", { id }); }
    catch (reason) { setError((reason as Error).message); }
  }
  async function copy(id: string) {
    setCopying(true); setError("");
    try { await request("saves.copy", { id }); setCopied(id); }
    catch (reason) { setError((reason as Error).message); }
    finally { setCopying(false); }
  }
  return <div className="save-finder" data-testid="save-finder">
    <div className="save-game"><div className="save-game-icon">{game.iconUrl ? <img src={game.iconUrl} alt="" /> : <HardDrive size={20} />}</div><div><strong>{game.name}</strong><span>Saves neste computador</span></div></div>
    <p className="save-intro">Encontre os locais conhecidos do jogo. Para instalações fora das lojas ou em outras pastas, indique onde ele está instalado.</p>
    <div className="save-toolbar">
      <button className="button secondary" disabled={busy} onClick={() => void scan(true, true)}><FolderOpen size={14} /> Escolher pasta do jogo</button>
      <button className="icon-button" aria-label="Buscar saves novamente" disabled={busy} onClick={() => void scan(manual)}><RefreshCw size={14} /></button>
    </div>
    {error && <p className="modal-error" role="alert">{error}</p>}
    <div className="save-results" aria-live="polite" aria-busy={busy}>
      {busy ? <div className="save-empty"><LoaderCircle size={23} className="spin" /><strong>Procurando saves…</strong><span>Consultando os locais deste jogo.</span></div> : result && <>
        <div className="save-result-heading"><span>{result.locations.length ? `${result.locations.length} ${result.locations.length === 1 ? "local encontrado" : "locais encontrados"}` : "Nenhum local encontrado"}</span>{result.installDetected && <small>{manual ? "Pasta escolhida" : "Instalação detectada"}</small>}</div>
        {result.installFolder && <p className="save-install-path" title={result.installFolder}>{result.installFolder}</p>}
        {result.locations.length ? <ul className="save-locations">{result.locations.map(location => <li key={location.id}>
          <div className="save-location-heading"><span><FolderOpen size={13} /> {location.kind === "registry" ? "Registro do Windows" : location.kind === "file" ? "Arquivo" : "Pasta"}</span><small className={location.basis === "candidate" ? "save-candidate" : ""}>{location.basis === "candidate" ? "Possível save" : "Local mapeado"}</small></div>
          {(location.lastModified || location.fileCount > 0) && <div className="save-location-meta">{location.fileCount > 0 && <span>{location.fileCount.toLocaleString(locale)} {location.fileCount === 1 ? "arquivo" : "arquivos"}</span>}{location.sizeBytes > 0 && <span>{new Intl.NumberFormat(locale, { style: "unit", unit: location.sizeBytes >= 1048576 ? "megabyte" : "kilobyte", maximumFractionDigits: 1 }).format(location.sizeBytes / (location.sizeBytes >= 1048576 ? 1048576 : 1024))}</span>}{location.lastModified && <span>Alterado em {new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" }).format(new Date(location.lastModified))}</span>}</div>}
          <code tabIndex={0}>{location.path}</code>
          <div className="save-location-buttons">{location.kind !== "registry" && <button className="save-open" onClick={() => void open(location.id)}><FolderOpen size={13} /> {location.kind === "file" ? "Mostrar na pasta" : "Abrir pasta"}</button>}<button className="save-copy" disabled={copying} onClick={() => void copy(location.id)} aria-label={`Copiar caminho de ${location.kind === "registry" ? "registro" : "save"}`}>{copied === location.id ? <Check size={13} /> : <Copy size={13} />}{copied === location.id ? "Copiado" : "Copiar caminho"}</button></div>
        </li>)}</ul> : <div className="save-empty"><Search size={23} /><strong>{result.catalogName ? "O save ainda não apareceu por aqui." : "Este jogo ainda não tem locais mapeados."}</strong><span>{result.catalogName ? "Talvez seja preciso abrir o jogo e salvar uma vez. Você também pode escolher a pasta de instalação para ampliar a busca." : "Escolha a pasta de instalação para procurar arquivos e pastas de save locais."}</span></div>}
        {(result.limited || result.rulesSkipped > 0) && <p className="save-note">{result.limited ? "A busca atingiu seu limite. Escolher a pasta exata pode ajudar." : "Alguns locais dependem da pasta de instalação ou usam padrões ainda não suportados."}</p>}
        {result.locations.some(location => location.basis === "candidate") && <p className="save-note">Os locais marcados como “Possível save” precisam ser conferidos por você.</p>}
      </>}
    </div>
    <div className="save-footer"><span>A busca não altera seus arquivos. Instalações modificadas podem usar outros locais.</span>{result && <small>Base: Ludusavi Manifest · {result.catalogCount.toLocaleString(locale)} jogos</small>}</div>
  </div>;
}
