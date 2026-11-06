/** Date inputs show an inclusive last day; API month boundaries are exclusive. */
export function financialPeriod(instant = new Date()) {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Amsterdam", year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(instant);
  const part = (name: string) => Number(parts.find(p => p.type === name)!.value);
  const start = new Date(Date.UTC(part("year"), part("month") - 1, 20));
  if (part("day") < 20) start.setUTCMonth(start.getUTCMonth() - 1);
  const end = new Date(start); end.setUTCMonth(end.getUTCMonth() + 1);
  const last = new Date(end); last.setUTCDate(last.getUTCDate() - 1);
  return { from: start.toISOString().slice(0, 10), to: last.toISOString().slice(0, 10), financialMonth: true };
}
