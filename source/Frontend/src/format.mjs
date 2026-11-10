export function clock(seconds) {
  const value = Math.max(0, Math.ceil(Number(seconds) || 0));
  const h = Math.floor(value / 3600),
    m = Math.floor(value / 60) % 60,
    s = value % 60;
  return h
    ? `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`
    : `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
}
export function duration(minutes) {
  const value = Math.max(0, Math.round(Number(minutes) || 0));
  return value < 60
    ? `${value} min`
    : value % 60
      ? `${Math.floor(value / 60)}h ${String(value % 60).padStart(2, "0")}min`
      : `${Math.floor(value / 60)}h`;
}
export function validMinutes(value) {
  return (
    /^\d+$/.test(String(value)) && Number(value) >= 1 && Number(value) <= 1440
  );
}
export function finishTime(seconds, now = Date.now(), locale = "pt-BR") {
  const value = Number(seconds);
  if (!Number.isFinite(value) || value <= 0) return "—";
  const start = new Date(now),
    end = new Date(Number(now) + value * 1000);
  if (Number.isNaN(end.getTime())) return "—";
  const sameDay = start.toDateString() === end.toDateString();
  return new Intl.DateTimeFormat(locale, {
    ...(sameDay ? {} : { day: "2-digit", month: "2-digit" }),
    ...(start.getFullYear() === end.getFullYear() ? {} : { year: "numeric" }),
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).format(end);
}
