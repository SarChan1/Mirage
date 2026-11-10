import { useEffect, useRef } from "react";
import { createPortal } from "react-dom";
import { X } from "lucide-react";
import type { SortOrder } from "./types";

export type DisplayAnchor = { x: number; y: number; origin: HTMLElement };
export function LibraryOptions({
  anchor,
  sort,
  onlySequence,
  hideQueued,
  onSort,
  onSequence,
  onHideQueued,
  onReset,
  onClose,
}: {
  anchor: DisplayAnchor;
  sort: SortOrder;
  onlySequence: boolean;
  hideQueued: boolean;
  onSort: (value: SortOrder) => void;
  onSequence: (value: boolean) => void;
  onHideQueued: (value: boolean) => void;
  onReset: () => void;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    ref.current?.querySelector<HTMLSelectElement>("select")?.focus();
    const outside = (event: Event) => {
      if (!ref.current?.contains(event.target as Node)) onClose();
    };
    document.addEventListener("pointerdown", outside);
    document.addEventListener("focusin", outside);
    window.addEventListener("resize", onClose);
    return () => {
      document.removeEventListener("pointerdown", outside);
      document.removeEventListener("focusin", outside);
      window.removeEventListener("resize", onClose);
    };
  }, [onClose]);
  return createPortal(
    <div
      ref={ref}
      className="library-options"
      role="dialog"
      aria-label="Exibição da biblioteca"
      style={{
        top: Math.max(40, Math.min(anchor.y, innerHeight - 290)),
        left: Math.max(10, Math.min(anchor.x - 250, innerWidth - 260)),
      }}
      onKeyDown={(event) => {
        if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) { event.preventDefault(); event.stopPropagation(); }
        if (event.key === "Escape") {
          event.preventDefault();
          event.stopPropagation();
          onClose();
          anchor.origin.focus();
        }
      }}
    >
      <div className="display-heading">
        <strong>Exibição</strong>
        <button
          className="icon-button"
          aria-label="Fechar opções de exibição"
          onClick={onClose}
        >
          <X size={14} />
        </button>
      </div>
      <label className="field-label" htmlFor="library-sort">
        Ordenar jogos
      </label>
      <select
        id="library-sort"
        value={sort}
        onChange={(event) => onSort(event.target.value as SortOrder)}
      >
        <option value="default">Padrão / relevância</option>
        <option value="nameAsc">Nome · A–Z</option>
        <option value="nameDesc">Nome · Z–A</option>
      </select>
      <div className="display-filter-group">
        <label>
          <input
            type="checkbox"
            checked={onlySequence}
            onChange={(event) => onSequence(event.target.checked)}
          />
          Apenas com sequência
        </label>
        <label>
          <input
            type="checkbox"
            checked={hideQueued}
            onChange={(event) => onHideQueued(event.target.checked)}
          />
          Ocultar jogos já na lista
        </label>
      </div>
      <button
        className="display-reset"
        onClick={onReset}
        disabled={!onlySequence && !hideQueued && sort === "default"}
      >
        Restaurar padrão
      </button>
    </div>,
    document.body,
  );
}
