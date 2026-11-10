import { useEffect, useId, useMemo, useState } from "react";
import { ArrowUpDown, CornerDownLeft, Search } from "lucide-react";
import "./preferences.css";
import { useI18n } from "./i18n";

export type QuickAction = {
  id: string;
  title: string;
  detail: string;
  keywords?: string;
  shortcut?: string;
  disabled?: boolean;
  run: () => void;
};
const normalize = (value: string, locale: string) => value.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLocaleLowerCase(locale);

export function QuickActions({ actions }: { actions: QuickAction[] }) {
  const { locale } = useI18n();
  const [query, setQuery] = useState("");
  const [active, setActive] = useState("");
  const prefix = useId();
  const matches = useMemo(() => {
    const words = normalize(query.trim(), locale).split(/\s+/).filter(Boolean);
    return actions.filter(action => words.every(word => normalize(`${action.title} ${action.detail} ${action.keywords ?? ""}`, locale).includes(word)));
  }, [actions, query, locale]);
  const available = matches.filter(action => !action.disabled);
  const current = available.find(action => action.id === active) ?? available[0];
  useEffect(() => { if (current) document.getElementById(`${prefix}-${current.id}`)?.scrollIntoView({ block: "nearest" }); }, [current?.id, prefix]);
  function move(delta: number) {
    if (!available.length) return;
    const position = Math.max(0, available.findIndex(action => action.id === current?.id));
    setActive(available[(position + delta + available.length) % available.length].id);
  }
  return <div className="quick-actions" data-testid="quick-actions">
    <div className="quick-search"><Search size={16} /><input
      data-autofocus
      role="combobox"
      aria-label="Buscar ação"
      aria-expanded="true"
      aria-controls={`${prefix}-results`}
      aria-activedescendant={current ? `${prefix}-${current.id}` : undefined}
      aria-autocomplete="list"
      placeholder="O que você quer fazer?"
      value={query}
      onChange={event => { setQuery(event.target.value); setActive(""); }}
      onKeyDown={event => {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") { event.preventDefault(); move(event.key === "ArrowDown" ? 1 : -1); }
        if (event.key === "Enter") { event.preventDefault(); current?.run(); }
      }}
    /></div>
    <div className="quick-results" role="listbox" id={`${prefix}-results`} aria-label="Ações disponíveis">
      {matches.map(action => <button key={action.id} id={`${prefix}-${action.id}`} type="button" role="option" aria-selected={current?.id === action.id} aria-disabled={Boolean(action.disabled)} disabled={action.disabled} tabIndex={-1} onMouseEnter={() => { if (!action.disabled) setActive(action.id); }} onClick={action.run} className={current?.id === action.id ? "active" : ""} data-action-id={action.id}>
        <span><strong>{action.title}</strong><small>{action.detail}</small></span>{action.shortcut && <kbd>{action.shortcut}</kbd>}
      </button>)}
      {!matches.length && <div className="quick-empty">Nenhuma ação encontrada.</div>}
    </div>
    <div className="quick-footer"><span><ArrowUpDown size={12} /> navegar</span><span><CornerDownLeft size={12} /> executar</span><span>Esc fechar</span></div>
  </div>;
}
