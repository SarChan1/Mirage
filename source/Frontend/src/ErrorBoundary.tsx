import { Component, type ReactNode } from "react";

export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() {
    if (this.state.failed) return <div className="splash" role="alert">
      <h1>Vamos reabrir sua interface</h1>
      <p>A janela encontrou um problema. Sua sequência e sua sessão continuam no Mirage.</p>
      <button className="button primary" onClick={() => location.reload()}>Reabrir interface</button>
    </div>;
    return this.props.children;
  }
}
