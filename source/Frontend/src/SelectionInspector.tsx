import { CheckSquare2, Clock3, Plus, X } from "lucide-react";
import { duration, validMinutes } from "./format.mjs";
import type { Game } from "./types";

export function SelectionInspector({
  games,
  minutes,
  setMinutes,
  locked,
  overLimit,
  onRemove,
  onReview,
  onAdd,
  onCancel,
}: {
  games: Game[];
  minutes: string;
  setMinutes: (value: string) => void;
  locked: boolean;
  overLimit: boolean;
  onRemove: (game: Game) => void;
  onReview: () => void;
  onAdd: () => void;
  onCancel: () => void;
}) {
  const valid = validMinutes(minutes);
  return (
    <aside
      className="inspector selection-inspector"
      aria-label="Resumo da seleção"
    >
      <div className="inspector-top">
        <span>Seleção múltipla</span>
        <span className="selection-counter">{games.length} / 100</span>
      </div>
      <div className="inspector-scroll">
        <div className="selection-intro">
          <CheckSquare2 size={24} />
          <h2>
            {games.length ? "Sua próxima sequência." : "Escolha seus jogos."}
          </h2>
          <p>
            {games.length
              ? "Os títulos entram na lista nesta ordem."
              : "Marque os jogos na biblioteca. Você pode continuar pesquisando sem perder a seleção."}
          </p>
        </div>
        <ol className="selection-game-list">
          {games.map((game, index) => (
            <li key={game.id}>
              <span className="selection-game-index">
                {String(index + 1).padStart(2, "0")}
              </span>
              <div className="selection-game-art">
                <span>{game.initial}</span>
                {game.iconUrl && (
                  <img
                    src={game.iconUrl}
                    alt=""
                    onError={(event) =>
                      (event.currentTarget.style.visibility = "hidden")
                    }
                  />
                )}
              </div>
              <span className="selection-game-name">{game.name}</span>
              <button
                className="icon-button"
                disabled={locked}
                aria-label={`Retirar ${game.name} da seleção`}
                onClick={() => onRemove(game)}
              >
                <X size={13} />
              </button>
            </li>
          ))}
        </ol>
      </div>
      <div className="selection-summary">
        <label className="selection-time">
          <span>Tempo por jogo</span>
          <span>
            <input
              type="number"
              min={1}
              max={1440}
              step={1}
              aria-label="Duração da seleção por jogo"
              value={minutes}
              disabled={locked}
              onChange={(event) => setMinutes(event.target.value)}
            />
            <small>min</small>
          </span>
        </label>
        <div className="selection-total">
          <span>
            <Clock3 size={12} />
            Duração total
          </span>
          <strong>
            {valid ? duration(Number(minutes) * games.length) : "—"}
          </strong>
        </div>
        {overLimit && (
          <p className="selection-limit">
            A lista pode ter até 500 jogos. Reduza a seleção para adicionar.
          </p>
        )}
        <button
          className="button primary w-full"
          disabled={locked || !games.length || !valid || overLimit}
          onClick={onAdd}
        >
          <Plus size={16} />
          {games.length
            ? `Adicionar ${games.length} ${games.length === 1 ? "jogo" : "jogos"}`
            : "Adicionar seleção"}
        </button>
        <button
          className="button ghost w-full"
          disabled={!games.length || locked}
          onClick={onReview}
        >
          Revisar seleção
        </button>
        <button className="queue-link" disabled={locked} onClick={onCancel}>
          Cancelar seleção
        </button>
      </div>
      <div className="inspector-bottom">
        <span className="local-dot" />
        Ctrl Enter para adicionar
      </div>
    </aside>
  );
}
