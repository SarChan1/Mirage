import { useEffect, useRef, useState } from "react";
import { Check, Leaf } from "lucide-react";
import type { NativeState } from "./types";
import { validMinutes } from "./format.mjs";
import "./preferences.css";

export function PreferencesPanel({ preferences, onChange, storageWarning }: {
  preferences: NativeState["preferences"];
  onChange: (values: Record<string, unknown>) => Promise<boolean>;
  storageWarning?: string | null;
}) {
  const [minutes, setMinutes] = useState(String(preferences.minutes));
  const [pending, setPending] = useState<Set<string>>(new Set());
  const saving = pending.size > 0;
  const latest = useRef({ minutes, preferences, pending, onChange });
  latest.current = { minutes, preferences, pending, onChange };
  useEffect(() => {
    const flush = () => {
      const value = latest.current;
      if (validMinutes(value.minutes) && Number(value.minutes) !== value.preferences.minutes && !value.pending.has("minutes")) void value.onChange({ minutes: Number(value.minutes) });
    };
    const visibility = () => { if (document.hidden) flush(); };
    window.addEventListener("mirage.before-background", flush);
    document.addEventListener("visibilitychange", visibility);
    return () => { window.removeEventListener("mirage.before-background", flush); document.removeEventListener("visibilitychange", visibility); flush(); };
  }, []);
  useEffect(() => setMinutes(String(preferences.minutes)), [preferences.minutes]);
  async function change(values: Record<string, unknown>) {
    const updates = { ...values };
    if (validMinutes(minutes) && Number(minutes) !== preferences.minutes) updates.minutes = Number(minutes);
    const keys = Object.keys(updates);
    setPending(previous => { const next = new Set(previous); keys.forEach(key => next.add(key)); return next; });
    try { await onChange(updates); }
    finally { setPending(previous => { const next = new Set(previous); keys.forEach(key => next.delete(key)); return next; }); }
  }
  useEffect(() => {
    if (!validMinutes(minutes) || Number(minutes) === preferences.minutes || pending.has("minutes")) return;
    const timer = setTimeout(() => void change({ minutes: Number(minutes) }), 350);
    return () => clearTimeout(timer);
  }, [minutes, preferences.minutes, pending.has("minutes")]);
  const toggles = [
    { key: "showWindow", value: preferences.showWindow, title: "Janela auxiliar", description: "Abrir uma janela auxiliar nos novos jogos adicionados." },
    { key: "minimizeToTray", value: preferences.minimizeToTray, title: "Minimizar para a bandeja", description: "Ao minimizar, acessar o Mirage pelo ícone próximo ao relógio." },
    { key: "notifications", value: preferences.notifications, title: "Avisos da sequência", description: "Mostrar um aviso do Windows quando a lista terminar." },
  ];
  return <div className="preferences-panel" data-testid="preferences-panel">
    <p className="preferences-intro">Ajuste o Mirage ao seu ritmo. As mudanças são salvas neste computador.</p>
    <div className="preference-duration"><div><strong>Duração padrão</strong><span>Usada ao adicionar novos jogos à sequência.</span></div><label><input data-autofocus autoFocus aria-label="Duração padrão" type="number" min={1} max={1440} step={1} value={minutes} aria-invalid={!validMinutes(minutes)} onChange={event => setMinutes(event.target.value)} onBlur={() => { if (validMinutes(minutes) && Number(minutes) !== preferences.minutes) void change({ minutes: Number(minutes) }); }} onKeyDown={event => { if (event.key === "Enter") event.currentTarget.blur(); }} /><small>min</small></label></div>
    {!validMinutes(minutes) && <p className="preference-error" role="alert">Use um tempo inteiro de 1 a 1440 minutos.</p>}
    <div className="preference-toggles">{toggles.map(toggle => <div className="preference-row" key={toggle.key}><div><strong id={`preference-${toggle.key}`}>{toggle.title}</strong><span id={`preference-${toggle.key}-detail`}>{toggle.description}</span></div><button type="button" role="switch" aria-checked={toggle.value} aria-labelledby={`preference-${toggle.key}`} aria-describedby={`preference-${toggle.key}-detail`} aria-disabled={pending.has(toggle.key)} className={`preference-switch ${toggle.value ? "on" : ""}`} onClick={() => { if (!pending.has(toggle.key)) void change({ [toggle.key]: !toggle.value }); }}><span>{toggle.value && <Check size={10} />}</span></button></div>)}</div>
    <div className="preference-economy"><Leaf size={17} /><div><strong>Economia de memória ativa</strong><span>A interface é liberada após 15 segundos na bandeja. Sua sessão continua; ao reabrir, a busca e a seleção voltam.</span></div></div>
    <div className="preference-save-status" role={storageWarning ? "alert" : "status"}>{storageWarning ? "As alterações ainda não foram salvas. Confira o aviso na janela principal." : saving ? "Salvando…" : "Alterações salvas automaticamente"}</div>
  </div>;
}
