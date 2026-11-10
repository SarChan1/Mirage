import { createRoot } from "react-dom/client";
import { MotionConfig } from "motion/react";
import "@fontsource-variable/inter";
import "./styles.css";
import App from "./App";
import { ErrorBoundary } from "./ErrorBoundary";
import { I18nProvider } from "./i18n";
createRoot(document.getElementById("root")!).render(
  <I18nProvider>
    <MotionConfig reducedMotion="user">
      <ErrorBoundary><App /></ErrorBoundary>
    </MotionConfig>
  </I18nProvider>,
);
