const $ = (s) => document.querySelector(s);
const escapeHtml = (s) =>
  String(s ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const money = (n) =>
  new Intl.NumberFormat("en-CA", { style: "currency", currency: "CAD" }).format(
    n,
  );
const number = (n) => new Intl.NumberFormat("en-CA").format(n);
const date = (s) =>
  new Date(s).toLocaleDateString("en-CA", {
    timeZone: "UTC",
    month: "short",
    day: "numeric",
  });
const timestamp = (s) =>
  new Date(s).toLocaleString("en-CA", {
    timeZone: "UTC",
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
  }) + " UTC";
const day = (d = new Date()) => d.toISOString().slice(0, 10);
const paths = {
  overview: "M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z",
  municipalities: "M3 21h18M5 21V9l7-5 7 5v12M9 12v5m6-5v5M3 9h18",
  transactions: "M3 7h17l-4-4M21 17H4l4 4M20 7l-4 4M4 17l4-4",
  imports: "M12 3v12m-4-4 4 4 4-4M4 15v5h16v-5",
  health: "M2 12h5l3-8 4 16 3-8h5",
  errors: "M12 3 2 21h20L12 3zm0 6v5m0 3v1",
  refresh:
    "M20 7v5h-5M4 17v-5h5M5 8a8 8 0 0 1 13-2l2 3M19 16a8 8 0 0 1-13 2l-2-3",
  check: "m5 12 4 4 10-10",
  search: "M10 3a7 7 0 1 0 0 14 7 7 0 0 0 0-14zm5 12 6 6",
  clock: "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zm0 4v5l3 2",
  arrow: "M5 12h14m-5-5 5 5-5 5",
  copy: "M8 8h12v13H8zM16 8V3H3v13h5",
};
const icon = (name) =>
  `<svg viewBox="0 0 24 24" aria-hidden="true" class="svg-icon"><path d="${paths[name] || paths.overview}"/></svg>`;
document
  .querySelectorAll("[data-icon]")
  .forEach((el) => (el.innerHTML = icon(el.dataset.icon)));
let state = null,
  view = "overview",
  apiKey = "",
  loading = false,
  refreshPending = false,
  dialogSequence = 0,
  importKind = "payments";
const listState = {
  transactions: { query: "", page: 1 },
  errors: { query: "", category: "", page: 1 },
  imports: { query: "", category: "", page: 1 },
};
const names = {
  overview: [
    "Integration overview",
    "Monitor client integrations. Find issues. Keep migrations moving.",
  ],
  municipalities: [
    "Municipalities",
    "Review client rules and resolve configuration differences.",
  ],
  transactions: [
    "Transactions",
    "Inspect accepted records and trace municipal references.",
  ],
  imports: [
    "Imports & reconciliation",
    "Compare source data with committed records. Investigate every difference.",
  ],
  health: [
    "Integration health",
    "See which clients need attention and how their requests are performing.",
  ],
  errors: [
    "Error explorer",
    "Find the cause. Follow the correlation ID. Resolve the integration.",
  ],
};
async function api(path, options = {}) {
  const res = await fetch(path, {
    ...options,
    headers: { ...(apiKey ? { "X-Api-Key": apiKey } : {}), ...options.headers },
  });
  const text = await res.text();
  let data;
  try {
    data = text ? JSON.parse(text) : {};
  } catch {
    throw new Error(`HTTP ${res.status}: API returned an invalid response`);
  }
  if (!res.ok)
    throw new Error(
      `${data.errorCode || res.status}: ${safeMessage(data.message || "Request failed")}${data.correlationId ? " · " + safeMessage(data.correlationId) : ""}`,
    );
  return data;
}
// The server supplies public diagnostic messages. Defense in depth for display only.
function safeMessage(message) {
  return String(message ?? "")
    .replace(
      /["']?(?:password|pwd|api[-_ ]?key|authorization|access[-_ ]?token|secret)["']?\s*[:=]\s*(?:Bearer\s+)?(?:"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|[^\s;,]+)/gi,
      "[redacted]",
    )
    .replace(/Bearer\s+[A-Za-z0-9._~+\/-]+/gi, "Bearer [redacted]")
    .replace(
      /(?:Server|Data Source|User ID)\s*=.*$/gim,
      "[connection details redacted]",
    )
    .split("\n")
    .filter(
      (line) =>
        !/^\s*(at\s+|--- End of|(?:[\w]+\.)*[\w]+Exception\b)/.test(line),
    )
    .join(" ")
    .slice(0, 600);
}
function diagnostic(e) {
  const code = e.errorCode || "UNKNOWN";
  if (/DUPLICATE|CONFLICT|CONCURRENCY|ACTIVE_ACCOUNTS/.test(code))
    return {
      category: "Conflict",
      process: "Reference or configuration validation",
      action:
        "Compare the original payload and municipal reference. Replay an identical accepted request; use a new reference only for a different transaction.",
    };
  if (
    /ACCOUNT_NOT_FOUND|MUNICIPALITY_NOT_FOUND|ACCOUNT_TYPE|UNSUPPORTED_PAYMENT_TYPE/.test(
      code,
    )
  )
    return {
      category: "Client mapping",
      process: "Municipality / account mapping",
      action:
        "Check the municipal code, account number and enabled service. Confirm that the account belongs to this municipality and payment type.",
    };
  if (/CSV|IMPORT|FILE|ROW_LIMIT/.test(code))
    return {
      category: "Import format",
      process: "CSV migration",
      action:
        "Check the migration guide, required headers, quoting and file limits. Inspect a created batch for individual row outcomes.",
    };
  if (/UNAUTHORIZED|FORBIDDEN/.test(code))
    return {
      category: "Access",
      process: "API authentication",
      action:
        "Confirm access in API access. Keep credentials out of tickets, screenshots and client exports.",
    };
  if (/INTERNAL|DATABASE|UNAVAILABLE/.test(code))
    return {
      category: "System",
      process: "API processing",
      action:
        "Use the correlation ID to locate the request in restricted server logs. Confirm database connectivity and retry according to the integration guide.",
    };
  return {
    category: "Validation",
    process: "Business rule validation",
    action:
      "Compare the request with municipal rules: minimum amount, outstanding balance, enabled service and partial-payment policy.",
  };
}
function badge(s, tone) {
  const good = [
    "Accepted",
    "Imported",
    "RECONCILED",
    "Completed",
    "No failures",
  ];
  const warning = ["WARNING", "Skipped", "Needs attention"];
  const cls =
    tone ||
    (good.includes(s)
      ? "good"
      : warning.includes(s)
        ? "warning"
        : ["No requests", "Unknown"].includes(s)
          ? "neutral"
          : "error");
  const text =
    { RECONCILED: "Reconciled", WARNING: "Review required", FAILED: "Failed" }[
      s
    ] || s;
  return `<span class="badge ${cls}"><span class="badge-dot"></span>${escapeHtml(text)}</span>`;
}
function panel(title, sub, body, action = "", cls = "") {
  return `<section class="panel ${cls}"><div class="panel-head"><div><h2>${title}</h2>${sub ? `<p>${sub}</p>` : ""}</div>${action}</div>${body}</section>`;
}
function empty(title, detail, symbol = "search") {
  return `<div class="empty">${icon(symbol)}<h3>${title}</h3><p>${detail}</p></div>`;
}
function table(
  headers,
  rows,
  note = "",
  emptyTitle = "No records match",
  emptyDetail = "Try a different reporting window or clear your filters.",
) {
  return `<div class="table-wrap" tabindex="0" role="region" aria-label="${escapeHtml(headers[0])} table"><table><thead><tr>${headers.map((h) => `<th>${h}</th>`).join("")}</tr></thead><tbody>${rows.length ? rows.join("") : `<tr><td colspan="${headers.length}">${empty(emptyTitle, emptyDetail)}</td></tr>`}</tbody></table></div>${note ? `<div class="table-note">${note}</div>` : ""}`;
}
function selectedClients() {
  const code = $("#municipality").value;
  return state.municipalities.filter(
    (m) => !code || m.municipalityCode === code,
  );
}
function clientName(code) {
  return (
    state.municipalities.find((m) => m.municipalityCode === code)
      ?.municipalityName ||
    code ||
    "Unattributed request"
  );
}
function clientCell(m) {
  return `<div class="client-name"><span class="client-icon">${escapeHtml(
    m.municipalityName
      .split(" ")
      .map((x) => x[0])
      .join("")
      .slice(0, 2),
  )}</span><div><strong>${escapeHtml(m.municipalityName)}</strong><small class="mono">${escapeHtml(m.municipalityCode)}</small></div></div>`;
}
function healthStatus(m) {
  return !m
    ? badge("Unknown")
    : m.failedRequests
      ? badge("Needs attention")
      : m.totalApiRequests
        ? badge("No failures")
        : badge("No requests");
}
function rate(m) {
  return m.totalApiRequests ? `${m.successRate}%` : "—";
}
function kpis() {
  const m = state.metrics,
    active = selectedClients().filter(
      (c) => c.acceptedPaymentTypes.length,
    ).length;
  return `<div class="kpis">${[
    [
      "Transactions Today",
      number(state.today.metrics.successfulTransactions),
      "Accepted records · today, UTC",
      "transactions",
      "teal",
    ],
    [
      "Success Rate",
      rate(m),
      `${number(m.successfulRequests)} of ${number(m.totalApiRequests)} API requests · reporting window`,
      "health",
      m.failedRequests ? "amber" : "teal",
    ],
    [
      "Failed Transactions",
      number(m.failedTransactions),
      "Rejected payment requests · retries count",
      "errors",
      m.failedTransactions ? "red" : "teal",
    ],
    [
      "Active Municipalities",
      number(active),
      "Configured clients with enabled services",
      "municipalities",
      "blue",
    ],
  ]
    .map(
      ([label, value, detail, symbol, tone]) =>
        `<article class="kpi ${tone}"><div class="kpi-top"><span>${label}</span><span class="kpi-icon">${icon(symbol)}</span></div><div class="value">${value}</div><p>${detail}</p></article>`,
    )
    .join("")}</div>`;
}
function healthSummary() {
  const clients = selectedClients(),
    flagged = clients
      .filter((c) => state.performance[c.municipalityCode]?.failedRequests)
      .sort(
        (a, b) =>
          state.performance[b.municipalityCode].failedRequests -
          state.performance[a.municipalityCode].failedRequests,
      );
  const affected =
    flagged
      .slice(0, 3)
      .map(
        (c) =>
          `<b>${escapeHtml(c.municipalityName)}</b> (${number(state.performance[c.municipalityCode].failedRequests)} failed)`,
      )
      .join(" · ") +
    (flagged.length > 3 ? ` · ${flagged.length - 3} more clients` : "");
  const m = state.metrics;
  return `<div class="health-summary ${m.failedRequests ? "attention" : ""}"><div class="summary-icon">${icon(m.failedRequests ? "errors" : "health")}</div><div><strong>${m.failedRequests ? `${number(m.failedRequests)} failed integration requests need investigation` : m.totalApiRequests ? "No failed integration requests in this window" : "Waiting for integration activity"}</strong><p>${affected ? `${affected}. ` : ""}${m.totalApiRequests ? `${number(m.successfulTransactions)} accepted transactions · ${money(m.totalAmount)} recorded.` : "No executed requests in the selected reporting window."} ${state.imports.filter((b) => b.reconciliation.status !== "RECONCILED").length} visible migration batches require review.</p></div><button class="subtle" data-go="errors">Investigate errors ${icon("arrow")}</button></div>`;
}
function transactionVolume() {
  let from = $("#from").value || day(new Date(Date.now() - 27 * 86400000)),
    to = $("#to").value || day();
  // Bound the SVG, without claiming this latest-record snapshot is a full historical series.
  const start = new Date(from + "T00:00:00Z"),
    end = new Date(to + "T00:00:00Z");
  const days = Math.floor((end - start) / 86400000) + 1;
  const buckets = new Map();
  const points = Math.min(days, 28);
  for (let i = 0; i < points; i++) {
    const d = day(new Date(end.getTime() - (points - 1 - i) * 86400000));
    buckets.set(d, 0);
  }
  state.transactions.forEach((t) => {
    const d = day(new Date(t.createdAt));
    if (buckets.has(d)) buckets.set(d, buckets.get(d) + 1);
  });
  const data = [...buckets].map(([date, count]) => ({ date, count }));
  if (!data.some((x) => x.count))
    return empty(
      "No accepted transactions",
      "Choose another window or submit a synthetic record.",
      "transactions",
    );
  const max = Math.max(4, ...data.map((x) => x.count)),
    w = 680,
    h = 205,
    left = 34,
    top = 14,
    bottom = 168,
    step = 620 / data.length;
  return `<div class="chart-summary"><strong>${number(state.metrics.successfulTransactions)}</strong><span>accepted in this window</span><span class="chart-legend"><i></i>Accepted records</span></div><svg class="chart" viewBox="0 0 ${w} ${h}" role="img" aria-label="Accepted transaction records by UTC creation date. ${state.transactions.length} recent records available.">${[0, 1, 2, 3].map((i) => `<line x1="${left}" y1="${top + (i * (bottom - top)) / 3}" x2="662" y2="${top + (i * (bottom - top)) / 3}"/><text x="2" y="${top + 4 + (i * (bottom - top)) / 3}">${Math.round((max * (3 - i)) / 3)}</text>`).join("")}${data
    .map((x, i) => {
      const barH = (x.count / max) * (bottom - top),
        xPos = left + i * step + step * 0.2;
      return `<rect x="${xPos}" y="${bottom - barH}" width="${Math.max(2, step * 0.6)}" height="${barH}" rx="3" fill="${i === data.length - 1 ? "#125d67" : "#67aaa8"}"><title>${x.date}: ${x.count} accepted records</title></rect>${i % Math.max(1, Math.ceil(data.length / 6)) === 0 || i === data.length - 1 ? `<text x="${xPos}" y="193">${date(x.date)}</text>` : ""}`;
    })
    .join(
      "",
    )}</svg><p class="chart-note">UTC creation date · up to 28 days · latest ${state.transactions.length} accepted records${state.metrics.successfulTransactions > state.transactions.length ? " (partial series)" : ""}</p>`;
}
function failureReasons() {
  const groups = new Map();
  state.errors.forEach((e) =>
    groups.set(e.errorCode, (groups.get(e.errorCode) || 0) + 1),
  );
  const entries = [...groups].sort((a, b) => b[1] - a[1]),
    max = Math.max(1, ...entries.map((x) => x[1]));
  return `<div class="panel-body failure-reasons">${
    entries.length
      ? entries
          .slice(0, 4)
          .map(
            ([code, count]) =>
              `<button class="reason" data-reason="${escapeHtml(code)}"><div><span>${escapeHtml(code.replaceAll("_", " ").toLowerCase())}</span><strong>${number(count)}</strong></div><span class="reason-track"><i style="width:${(count / max) * 100}%"></i></span></button>`,
          )
          .join("")
      : empty(
          "No recorded errors",
          "No error records match this window.",
          "check",
        )
  }<p class="chart-note">Latest ${state.errors.length} error records${state.errors.some((e) => e.correlationId.startsWith("synthetic-")) ? " · includes labelled seed examples" : ""}. Select a reason to investigate.</p></div>`;
}
function performanceTable() {
  return table(
    [
      "Municipality",
      "Integration status",
      "Accepted",
      "Failed requests",
      "API success",
      "Avg. response",
    ],
    selectedClients().map((c) => {
      const m = state.performance[c.municipalityCode];
      return `<tr><td>${clientCell(c)}</td><td>${healthStatus(m)}</td><td class="numeric">${m ? number(m.successfulTransactions) : "—"}</td><td class="numeric ${m?.failedRequests ? "text-red" : ""}">${m ? number(m.failedRequests) : "—"}</td><td><div class="rate">${m ? rate(m) : "—"}${m?.totalApiRequests ? `<span class="rate-track"><i style="width:${m.successRate}%"></i></span>` : ""}</div></td><td class="numeric">${m?.totalApiRequests ? `${m.averageResponseMs} ms` : "—"}</td></tr>`;
    }),
    "Status reflects executed API requests in this window. No requests means health is unknown; accepted records can include seeded or imported data.",
  );
}
function municipalityTable() {
  return table(
    [
      "Municipality",
      "Enabled services",
      "Payment policy",
      "Minimum",
      "Configuration",
    ],
    selectedClients().map(
      (m) =>
        `<tr><td>${clientCell(m)}</td><td><div class="service-tags">${m.acceptedPaymentTypes.map((t) => `<span>${escapeHtml(t.replace(/([a-z])([A-Z])/g, "$1 $2"))}</span>`).join("")}</div></td><td>${m.allowPartialPayments ? "Partial payments enabled" : "Full balance required"}</td><td class="numeric">${money(m.minimumPayment)}</td><td><button class="button secondary small-button" data-edit="${escapeHtml(m.municipalityCode)}">Configure ${icon("arrow")}</button></td></tr>`,
    ),
  );
}
function transactionTable(rows) {
  return table(
    [
      "External reference",
      "Municipality / account",
      "Service",
      "Effective date",
      "Amount (CAD)",
      "Outcome",
    ],
    rows.map(
      (p) =>
        `<tr><td><strong class="mono">${escapeHtml(p.externalReference)}</strong><small>Recorded ${date(p.createdAt)}</small></td><td>${escapeHtml(p.municipalityName)}<small class="mono">${escapeHtml(p.accountNumber)}</small></td><td>${escapeHtml(p.paymentType.replace(/([a-z])([A-Z])/g, "$1 $2"))}</td><td>${escapeHtml(p.transactionDate)}</td><td class="numeric">${money(p.amount)}</td><td>${badge(p.status)}</td></tr>`,
    ),
    "Accepted records only. Failed requests are available in Error explorer.",
    "No accepted records",
    "Clear the search, choose another window or submit a synthetic record.",
  );
}
function errorTable(rows) {
  return table(
    ["Time (UTC)", "Municipality", "Category / error code", "Safe message", ""],
    rows.map(
      (e) =>
        `<tr><td class="nowrap">${date(e.createdAt)}<small>${new Date(e.createdAt).toISOString().slice(11, 19)}${e.correlationId.startsWith("synthetic-") ? " · seed example" : ""}</small></td><td>${escapeHtml(clientName(e.municipalityCode))}<small class="mono">${escapeHtml(e.municipalityCode || "No municipal context")}</small></td><td>${badge(diagnostic(e).category, "neutral")}<small class="error-code">${escapeHtml(e.errorCode)}</small></td><td class="message-cell">${escapeHtml(safeMessage(e.message))}</td><td><button class="row-action" data-error="${escapeHtml(e.id)}" aria-label="Investigate ${escapeHtml(e.errorCode)} for ${escapeHtml(clientName(e.municipalityCode))}">Investigate ${icon("arrow")}</button></td></tr>`,
    ),
    "Latest 100 matching errors. Seed examples are labelled; errors are events, not a resolution queue.",
    "No errors match",
    "No recorded errors in this window, or your search has no matches. Clear filters to widen the investigation.",
  );
}
function importsTable(rows) {
  return table(
    [
      "Batch / file",
      "Source rows",
      "Accepted",
      "Rejected",
      "Source amount",
      "Imported amount",
      "Difference",
      "Reconciliation",
    ],
    rows.map((b) => {
      const r = b.reconciliation;
      return `<tr><td><button class="batch-link" data-batch="${b.id}">${escapeHtml(b.fileName)}</button><small>${escapeHtml(b.kind)} · ${date(b.createdAt)} · <span class="mono">${b.id.slice(0, 8)}</span></small></td><td class="numeric">${number(r.sourceRecordCount)}</td><td class="numeric text-teal">${number(r.importedRecordCount)}</td><td class="numeric ${r.rejectedRecordCount ? "text-red" : ""}">${number(r.rejectedRecordCount)}<small>${r.skippedRecordCount ? `${r.skippedRecordCount} skipped` : ""}</small></td><td class="numeric">${money(r.totalSourceAmount)}${!r.sourceAmountComplete ? '<small class="text-amber">Known amounts only</small>' : ""}</td><td class="numeric">${money(r.totalImportedAmount)}</td><td class="numeric ${r.difference ? "text-amber" : ""}">${money(r.difference)}</td><td>${badge(r.status)}</td></tr>`;
    }),
    "Source − newly imported = difference. Skipped duplicates contribute no new imported amount. Latest 30 matching batches.",
    "No migration batches",
    "Import a synthetic CSV or clear filters to see earlier batches.",
  );
}
function changes() {
  const entries = [
    ...state.transactions.map((t) => ({
      at: t.createdAt,
      label: "Transaction accepted",
      detail: `${clientName(t.municipalityCode)} · ${money(t.amount)}`,
      symbol: "check",
    })),
    ...state.imports.map((b) => ({
      at: b.createdAt,
      label: "Migration batch processed",
      detail: `${b.fileName} · ${b.reconciliation.status.toLowerCase()}`,
      symbol: "imports",
      batch: b.id,
    })),
    ...state.errors
      .filter((e) => !e.correlationId.startsWith("synthetic-"))
      .map((e) => ({
        at: e.createdAt,
        label: "Integration request rejected",
        detail: `${clientName(e.municipalityCode)} · ${e.errorCode}`,
        symbol: "errors",
        error: e.id,
      })),
  ].sort((a, b) => new Date(b.at) - new Date(a.at));
  const seen = new Set();
  const items = entries.filter((i) => {
    if (seen.has(i.symbol)) return false;
    seen.add(i.symbol);
    return true;
  });
  return `<div class="change-list">${items.length ? items.map((i) => `<div class="change"><span class="change-icon ${i.symbol === "errors" ? "red" : ""}">${icon(i.symbol)}</span><div><strong>${i.label}</strong><p>${escapeHtml(i.detail)}</p><small>${timestamp(i.at)}</small></div>${i.batch || i.error ? `<button class="icon-button" ${i.batch ? `data-batch="${i.batch}"` : `data-error="${i.error}"`} aria-label="Inspect recent ${i.batch ? "batch" : "error"}">${icon("arrow")}</button>` : ""}</div>`).join("") : empty("No recent activity", "Transactions, batches and executed request errors appear here.", "clock")}</div>`;
}
function listTools(type) {
  const s = listState[type];
  const categories =
    type === "errors"
      ? [...new Set(state.errors.map((e) => diagnostic(e).category))].sort()
      : ["RECONCILED", "WARNING", "FAILED"];
  return `<div class="list-tools"><label class="search-field">${icon("search")}<span class="sr-only">Search ${type}</span><input id="list-search" type="search" placeholder="${type === "errors" ? "Search code, message or correlation ID…" : type === "transactions" ? "Search reference, municipality or account…" : "Search file name or batch ID…"}" value="${escapeHtml(s.query)}"></label>${type !== "transactions" ? `<label><span class="sr-only">${type === "errors" ? "Error category" : "Reconciliation status"}</span><select id="list-category"><option value="">${type === "errors" ? "All categories" : "All reconciliation statuses"}</option>${categories.map((c) => `<option value="${escapeHtml(c)}" ${s.category === c ? "selected" : ""}>${escapeHtml({ RECONCILED: "Reconciled", WARNING: "Review required", FAILED: "Failed" }[c] || c)}</option>`).join("")}</select></label>` : ""}<span id="list-count"></span></div><div id="record-list"></div>`;
}
function bindActions(root = document) {
  root
    .querySelectorAll("[data-go]")
    .forEach((b) => (b.onclick = () => navigate(b.dataset.go)));
  root
    .querySelectorAll("[data-edit]")
    .forEach(
      (b) =>
        (b.onclick = () =>
          configurationDialog(
            state.municipalities.find(
              (m) => m.municipalityCode === b.dataset.edit,
            ),
          )),
    );
  root
    .querySelectorAll("[data-batch]")
    .forEach((b) => (b.onclick = () => batchDialog(b.dataset.batch)));
  root
    .querySelectorAll("[data-error]")
    .forEach((b) => (b.onclick = () => errorDialog(b.dataset.error)));
  root.querySelectorAll("[data-reason]").forEach(
    (b) =>
      (b.onclick = () => {
        listState.errors.query = b.dataset.reason;
        listState.errors.category = "";
        listState.errors.page = 1;
        navigate("errors");
      }),
  );
}
function updateList() {
  const s = listState[view];
  if (!s || !$("#record-list")) return;
  const source = state[view],
    query = s.query.toLowerCase();
  const rows = source.filter((r) => {
    const hay =
      view === "errors"
        ? [
            r.errorCode,
            safeMessage(r.message),
            r.correlationId,
            r.municipalityCode,
            clientName(r.municipalityCode),
          ]
        : view === "imports"
          ? [r.fileName, r.id, r.kind]
          : [
              r.externalReference,
              r.municipalityName,
              r.accountNumber,
              r.paymentType,
            ];
    return (
      (!query || hay.join(" ").toLowerCase().includes(query)) &&
      (!s.category ||
        (view === "errors"
          ? diagnostic(r).category === s.category
          : r.reconciliation.status === s.category))
    );
  });
  const size = 10,
    pages = Math.max(1, Math.ceil(rows.length / size));
  s.page = Math.min(s.page, pages);
  const pageRows = rows.slice((s.page - 1) * size, s.page * size);
  $("#list-count").textContent =
    `${number(rows.length)} ${view === "imports" ? "batches" : "records"}`;
  $("#record-list").innerHTML =
    {
      transactions: transactionTable,
      errors: errorTable,
      imports: importsTable,
    }[view](pageRows) +
    `<div class="pagination"><span>${rows.length ? `${(s.page - 1) * size + 1}–${Math.min(s.page * size, rows.length)} of ${rows.length}` : "0 records"} · loaded snapshot</span><div><button id="list-prev" class="button secondary small-button" ${s.page === 1 ? "disabled" : ""} aria-label="Previous page">←</button><span>Page ${s.page} of ${pages}</span><button id="list-next" class="button secondary small-button" ${s.page === pages ? "disabled" : ""} aria-label="Next page">→</button></div></div>`;
  $("#list-prev").onclick = () => {
    s.page--;
    updateList();
  };
  $("#list-next").onclick = () => {
    s.page++;
    updateList();
  };
  bindActions($("#record-list"));
}
function render() {
  $("#title").textContent = names[view][0];
  $("#subtitle").textContent = names[view][1];
  $("#crumb").textContent = view === "overview" ? "Overview" : names[view][0];
  document.querySelectorAll("nav [data-view]").forEach((b) => {
    b.classList.toggle("active", b.dataset.view === view);
    if (b.dataset.view === view) b.setAttribute("aria-current", "page");
    else b.removeAttribute("aria-current");
  });
  if (!state) return;
  $("#error-nav-count").textContent = state.errors.length;
  $("#error-nav-count").hidden = !state.errors.length;
  let html = "";
  if (view === "overview")
    html =
      healthSummary() +
      kpis() +
      `<div class="chart-grid">${panel("Transaction volume over time", "Accepted payment records across the reporting window", `<div class="panel-body">${transactionVolume()}</div>`, badge("Accepted", "good"))}${panel("Failure reasons", "Top 4 codes from the loaded error records", failureReasons(), '<button class="subtle" data-go="errors">Explore ' + icon("arrow") + "</button>")}</div>` +
      panel(
        "Municipality performance",
        "Client-level outcomes · selected reporting window",
        performanceTable(),
        '<button class="subtle" data-go="health">Integration health ' +
          icon("arrow") +
          "</button>",
      ) +
      panel(
        "Recent integration errors",
        "Safe diagnostics with traceable correlation IDs",
        errorTable(state.errors.slice(0, 4)),
        '<button class="subtle" data-go="errors">View all errors ' +
          icon("arrow") +
          "</button>",
      ) +
      panel(
        "Recent changes",
        "Latest recorded activity per process · configuration history is not available",
        changes(),
      );
  if (view === "municipalities")
    html =
      `<div class="callout"><div>${icon("municipalities")}<div><strong>Client rules apply across API requests and CSV imports</strong><p>Review enabled services, payment minimums and balance policy before troubleshooting a client.</p></div></div><button id="new-client" class="button">+ Add municipality</button></div>` +
      panel(
        "Municipal configuration",
        `${selectedClients().length} clients · version checks protect against stale updates`,
        municipalityTable(),
      );
  if (view === "transactions")
    html =
      `<div class="compact-stats"><div><span>Accepted in window</span><strong>${number(state.metrics.successfulTransactions)}</strong></div><div><span>Recorded amount</span><strong>${money(state.metrics.totalAmount)}</strong></div><div><span>Failed payment requests</span><strong class="text-red">${number(state.metrics.failedTransactions)}</strong></div></div>` +
      panel(
        "Accepted transactions",
        "Unique municipal references · latest 200 matching accepted records",
        listTools("transactions"),
        '<button id="simulate" class="button">+ Simulate payment</button>',
      );
  if (view === "imports") {
    const batches = state.imports,
      good = batches.filter(
        (b) => b.reconciliation.status === "RECONCILED",
      ).length,
      rejected = batches.reduce(
        (n, b) => n + b.reconciliation.rejectedRecordCount,
        0,
      );
    html =
      `<div class="compact-stats"><div><span>Loaded batches</span><strong>${batches.length}</strong></div><div><span>Reconciled</span><strong class="text-teal">${good}</strong></div><div><span>Need review</span><strong class="text-amber">${batches.length - good}</strong></div><div><span>Rejected source rows</span><strong class="text-red">${rejected}</strong></div></div>` +
      panel(
        "Migration batches",
        "Inspect a batch to compare totals and resolve rejected or skipped rows",
        listTools("imports"),
      ) +
      panel(
        "Import synthetic data",
        "Accounts first, then payments · 2 MB maximum · 5,000 source rows",
        `<div class="panel-body"><form id="upload" class="upload"><label>Import kind<select name="kind"><option value="payments" ${importKind === "payments" ? "selected" : ""}>Payment records</option><option value="accounts" ${importKind === "accounts" ? "selected" : ""}>Accounts</option></select></label><label class="file-field">Synthetic CSV<input name="file" type="file" accept=".csv,text/csv" required></label><button class="button">${icon("imports")}Validate & import</button></form><p class="chart-note">Use the documented CSV headers. Invalid rows are recorded; a malformed file is rejected before a batch is created.</p><div id="upload-result" role="status"></div></div>`,
      );
  }
  if (view === "health")
    html =
      healthSummary() +
      kpis() +
      panel(
        "Municipality performance",
        "Status is based on failed API requests, not database connectivity",
        performanceTable(),
      ) +
      `<div class="chart-grid">${panel("Executed request volume", "Monitoring and health polling are excluded", `<div class="panel-body">${requestActivity()}</div>`)}${panel("Integration signals", "Selected reporting window", `<div class="signal-list"><div><span>Successful API requests</span><strong class="text-teal">${number(state.metrics.successfulRequests)}</strong></div><div><span>Failed API requests</span><strong class="text-red">${number(state.metrics.failedRequests)}</strong></div><div><span>Validation failures</span><strong>${number(state.metrics.validationFailures)}</strong></div><div><span>Average request duration</span><strong>${state.metrics.totalApiRequests ? state.metrics.averageResponseMs + " ms" : "—"}</strong></div></div>`)}</div>`;
  if (view === "errors")
    html =
      `<div class="investigation-intro"><span class="summary-icon">${icon("errors")}</span><div><strong>Start with a failure. Finish with a traceable cause.</strong><p>Search an error code or correlation ID, filter a category, then inspect safe diagnostics and recommended checks.</p></div><span class="badge neutral">${state.errors.length} loaded errors</span></div>` +
      panel(
        "Integration errors",
        "Latest 100 matching error records · category and process are inferred from the error code",
        listTools("errors"),
      );
  $("#content").innerHTML = html;
  bindActions();
  if ($("#new-client")) $("#new-client").onclick = () => configurationDialog();
  if ($("#simulate")) $("#simulate").onclick = paymentDialog;
  if ($("#upload")) $("#upload").onsubmit = upload;
  if ($("#list-search")) {
    $("#list-search").oninput = (e) => {
      listState[view].query = e.target.value;
      listState[view].page = 1;
      updateList();
    };
    if ($("#list-category"))
      $("#list-category").onchange = (e) => {
        listState[view].category = e.target.value;
        listState[view].page = 1;
        updateList();
      };
    updateList();
  }
}
function requestActivity() {
  if (!state.daily.length)
    return empty(
      "No executed requests",
      "This window has no recorded integration requests.",
      "health",
    );
  const max = Math.max(1, ...state.daily.map((d) => d.accepted + d.rejected));
  return `<div class="request-days">${state.daily
    .slice(-7)
    .map(
      (d) =>
        `<div class="request-day"><span>${date(d.date)}</span><div class="request-track"><i class="request-ok" style="width:${(d.accepted / max) * 100}%"></i><i class="request-failed" style="width:${(d.rejected / max) * 100}%"></i></div><strong>${d.accepted + d.rejected}</strong></div>`,
    )
    .join(
      "",
    )}</div><div class="legend"><span><i class="legend-dot"></i>Successful requests</span><span><i class="legend-dot red"></i>Failed requests</span></div><p class="chart-note">Up to 7 days with recorded activity. Request retries count separately.</p>`;
}
function skeleton() {
  return `<div class="sr-only">Loading integration data</div><div class="kpis skeleton" aria-hidden="true">${Array.from({ length: 4 }, () => '<div class="kpi"><i></i><b></b><i></i></div>').join("")}</div><div class="panel skeleton skeleton-panel" aria-hidden="true"><i></i><b></b><b></b><b></b></div>`;
}
function queryParams() {
  const p = new URLSearchParams();
  for (const id of ["municipality", "from", "to"])
    if ($("#" + id).value) p.set(id, $("#" + id).value);
  return p;
}
async function refresh() {
  if (loading) {
    refreshPending = true;
    return;
  }
  const params = queryParams();
  if (
    params.get("from") &&
    params.get("to") &&
    params.get("from") > params.get("to")
  ) {
    showNotice(
      "Choose a valid date range. From date must be on or before To date. The previous snapshot remains visible.",
    );
    $("#updated").textContent = "Showing previous snapshot";
    return;
  }
  loading = true;
  $("#refresh").disabled = true;
  $("#refresh").classList.add("refreshing");
  $("#content").setAttribute("aria-busy", "true");
  $("#updated").textContent = state ? "Updating…" : "Loading integration data…";
  if (!state) $("#content").innerHTML = skeleton();
  try {
    const snapshot = await api("/api/monitoring?" + params);
    const todayParams = new URLSearchParams({ from: day(), to: day() });
    if (params.get("municipality"))
      todayParams.set("municipality", params.get("municipality"));
    const clients = snapshot.municipalities.filter(
      (m) =>
        !params.get("municipality") ||
        m.municipalityCode === params.get("municipality"),
    );
    const performance = {};
    // Bounded concurrency for client-level metrics, using existing read-only endpoints.
    const [today] = await Promise.all([
      api("/api/monitoring?" + todayParams),
      (async () => {
        for (let start = 0; start < clients.length; start += 3)
          await Promise.all(
            clients.slice(start, start + 3).map(async (c) => {
              const p = new URLSearchParams(params);
              p.set("municipality", c.municipalityCode);
              try {
                performance[c.municipalityCode] = (
                  await api("/api/monitoring?" + p)
                ).metrics;
              } catch {
                performance[c.municipalityCode] = null;
              }
            }),
          );
      })(),
    ]);
    if (params.toString() !== queryParams().toString()) {
      refreshPending = true;
      return;
    }
    state = { ...snapshot, today, performance };
    const selected = $("#municipality").value;
    $("#municipality").innerHTML =
      '<option value="">All municipalities</option>' +
      state.municipalities
        .map(
          (m) =>
            `<option value="${escapeHtml(m.municipalityCode)}">${escapeHtml(m.municipalityName)}</option>`,
        )
        .join("");
    $("#municipality").value = selected;
    $("#notice").hidden = true;
    $("#connection").textContent = "API connected";
    $("#connection").className = "connection connected";
    $("#updated").textContent =
      "Updated " +
      new Date().toLocaleTimeString("en-CA", {
        hour: "2-digit",
        minute: "2-digit",
        hour12: false,
        timeZone: "UTC",
      }) +
      " UTC";
    render();
  } catch (e) {
    showNotice(
      e.message +
        (state
          ? " The previous snapshot remains visible and does not reflect newly selected filters."
          : ""),
    );
    $("#connection").textContent = "Data unavailable";
    $("#connection").className = "connection unavailable";
    $("#updated").textContent = state
      ? "Showing last successful snapshot"
      : "Connection failed";
    if (!state)
      $("#content").innerHTML = empty(
        "Unable to load the workspace",
        "Check API access and connectivity, then select Refresh data.",
        "errors",
      );
  } finally {
    loading = false;
    $("#refresh").disabled = false;
    $("#refresh").classList.remove("refreshing");
    $("#content").setAttribute("aria-busy", "false");
    if (refreshPending) {
      refreshPending = false;
      refresh();
    }
  }
}
function showNotice(message) {
  $("#notice").hidden = false;
  $("#notice").textContent = safeMessage(message);
}
function navigate(v) {
  view = names[v] ? v : "overview";
  location.hash = view;
  $("#sidebar").classList.remove("mobile-open");
  $("#menu-toggle").setAttribute("aria-expanded", "false");
  render();
  window.scrollTo(0, 0);
}
function dialog(title, body, cls = "") {
  dialogSequence++;
  if ($("#modal").open) $("#modal").close();
  $("#modal").className = cls;
  $("#modal-title").textContent = title;
  $("#modal-eyebrow").textContent =
    cls === "inspector"
      ? "SAFE DIAGNOSTICS"
      : cls === "batch-dialog"
        ? "MIGRATION INVESTIGATION"
        : "WORKSPACE";
  $("#modal-body").innerHTML = body;
  $("#modal-result").textContent = "";
  $("#modal-result").className = "";
  $("#modal").showModal();
}
function errorDialog(id) {
  const e = state.errors.find((e) => String(e.id) === id);
  if (!e) return;
  const d = diagnostic(e);
  dialog(
    "Investigate integration error",
    `<div class="diagnostic-banner">${icon("errors")}<div><strong>${escapeHtml(e.errorCode)}</strong><span>${escapeHtml(d.category)}${e.correlationId.startsWith("synthetic-") ? " · synthetic seed example" : ""}</span></div></div><dl class="diagnostic-fields"><div><dt>Timestamp</dt><dd>${timestamp(e.createdAt)}</dd></div><div><dt>Municipality</dt><dd>${escapeHtml(clientName(e.municipalityCode))}<small class="mono">${escapeHtml(e.municipalityCode || "Not recorded")}</small></dd></div><div><dt>Correlation ID</dt><dd class="correlation"><code>${escapeHtml(safeMessage(e.correlationId))}</code><button class="icon-button" id="copy-correlation" aria-label="Copy correlation ID">${icon("copy")}</button></dd></div><div><dt>Error category <span class="inferred">inferred</span></dt><dd>${escapeHtml(d.category)}</dd></div><div><dt>Process <span class="inferred">inferred</span></dt><dd>${escapeHtml(d.process)}</dd></div><div><dt>Endpoint</dt><dd class="muted">Not recorded in this API response</dd></div></dl><div class="safe-message"><h3>Sanitized message</h3><p>${escapeHtml(safeMessage(e.message))}</p></div><div class="next-check"><h3>Recommended checks</h3><p>${escapeHtml(d.action)}</p><small>Use this correlation ID to locate the precise endpoint in authorized server logs. Never paste credentials or stack traces into a client ticket.</small></div><div class="form-actions"><button class="button secondary" id="error-client">Review client rules</button><a class="button" href="/swagger" target="_blank" rel="noopener">Open API docs ↗</a></div>`,
    "inspector",
  );
  $("#copy-correlation").onclick = async () => {
    try {
      await navigator.clipboard.writeText(e.correlationId);
      $("#modal-result").textContent = "Correlation ID copied.";
    } catch {
      $("#modal-result").textContent =
        "Copy unavailable. Select and copy the correlation ID above.";
    }
  };
  $("#error-client").onclick = () => {
    const m = state.municipalities.find(
      (m) => m.municipalityCode === e.municipalityCode,
    );
    if (m) configurationDialog(m);
    else {
      $("#modal").close();
      navigate("municipalities");
    }
  };
}

function configurationDialog(m) {
  dialog(
    m ? "Configure " + m.municipalityName : "Add municipality",
    `<form id="client-form"><div class="form-grid"><label>Municipality code<input name="code" required maxlength="40" value="${escapeHtml(m?.municipalityCode || "")}" ${m ? "readonly" : ""}></label><label>Name<input name="name" required maxlength="120" value="${escapeHtml(m?.municipalityName || "")}"></label><label>Currency<select name="currency"><option>CAD</option></select></label><label>Minimum payment<input name="minimum" type="number" min="0.01" step="0.01" required value="${m?.minimumPayment || 5}"></label><div class="wide checks"><label><input name="partial" type="checkbox" ${m?.allowPartialPayments ? "checked" : ""}> Allow partial payments</label></div><div class="wide checks">${["PropertyTax", "Utility", "ParkingTicket", "BusinessLicence", "Permit"].map((t) => `<label><input name="types" type="checkbox" value="${t}" ${m?.acceptedPaymentTypes.includes(t) ? "checked" : ""}>${t}</label>`).join("")}</div></div><div class="form-actions"><button class="button">Save configuration</button></div></form>`,
  );
  $("#client-form").onsubmit = async (e) => {
    e.preventDefault();
    const f = new FormData(e.target);
    const body = {
      municipalityCode: f.get("code"),
      municipalityName: f.get("name"),
      currency: "CAD",
      minimumPayment: Number(f.get("minimum")),
      allowPartialPayments: f.has("partial"),
      acceptedPaymentTypes: f.getAll("types"),
      version: m?.version,
    };
    const saved = await submit(() =>
      api(
        "/api/municipalities" +
          (m ? "/" + encodeURIComponent(m.municipalityCode) : ""),
        {
          method: m ? "PUT" : "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(body),
        },
      ),
    );
    if (saved) {
      m = saved;
      if (e.target.isConnected) e.target.elements.code.readOnly = true;
    }
  };
}
async function paymentDialog() {
  let accounts;
  try {
    accounts = { items: [] };
    let page = 1,
      response;
    do {
      response = await api(
        "/api/monitoring/accounts?" +
          new URLSearchParams({
            page: page++,
            pageSize: 200,
            ...($("#municipality").value
              ? { municipality: $("#municipality").value }
              : {}),
          }),
      );
      accounts.items.push(...response.items);
    } while (response.items.length && accounts.items.length < response.total);
  } catch (e) {
    $("#notice").textContent = e.message;
    $("#notice").hidden = false;
    return;
  }
  if (!accounts.items.some((a) => a.balance > 0)) {
    dialog(
      "Simulate a payment",
      "<p>No outstanding accounts match the selected municipality. Import accounts or choose another municipality.</p>",
    );
    return;
  }
  dialog(
    "Simulate a payment",
    `<form id="payment-form"><div class="form-grid"><label class="wide">Synthetic account<select name="account">${accounts.items
      .filter((a) => a.balance > 0)
      .map(
        (a, i) =>
          `<option value="${i}">${escapeHtml(a.municipalityCode + " / " + a.accountNumber)} · balance snapshot ${money(a.balance)}</option>`,
      )
      .join(
        "",
      )}</select></label><label>Amount (CAD)<input name="amount" type="number" step="0.01" min="0.01" value="25" required></label><label>Transaction date (UTC)<input name="date" type="date" value="${new Date().toISOString().slice(0, 10)}" required></label><label class="wide">External reference<input name="reference" value="DEMO-${crypto.randomUUID().slice(0, 8)}" maxlength="100" required></label></div><p class="chart-note">Balance is a snapshot. Server validation checks the current balance and client rules. An identical reference retry replays the accepted record.</p><div class="form-actions"><button class="button">Submit synthetic record</button></div></form>`,
  );
  const eligible = accounts.items.filter((a) => a.balance > 0);
  $("#payment-form").onsubmit = async (e) => {
    e.preventDefault();
    const f = new FormData(e.target),
      a = eligible[Number(f.get("account"))];
    await submit(() =>
      api("/api/payments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          municipalityCode: a.municipalityCode,
          accountNumber: a.accountNumber,
          paymentType: a.paymentType,
          amount: Number(f.get("amount")),
          transactionDate: f.get("date"),
          externalReference: f.get("reference"),
        }),
      }),
    );
  };
}
async function submit(fn) {
  const form = $("#modal form"),
    resultNode = $("#modal-result"),
    buttons = form.querySelectorAll("button");
  buttons.forEach((b) => (b.disabled = true));
  resultNode.className = "";
  resultNode.textContent = "Submitting…";
  try {
    const result = await fn();
    if (form.isConnected) {
      resultNode.className = "result-success";
      resultNode.textContent = result.transactionId
        ? `${result.replayed ? "Existing transaction replayed" : "Synthetic transaction accepted"}. Reference: ${result.externalReference || form.elements.reference?.value || ""}. Transaction ID: ${result.transactionId}.`
        : "Municipal configuration saved. Updated rules will apply to subsequent requests.";
    }
    await refresh();
    return result;
  } catch (e) {
    if (form.isConnected) {
      resultNode.className = "result-error";
      resultNode.textContent = safeMessage(e.message);
    }
    await refresh();
    return null;
  } finally {
    buttons.forEach((b) => (b.disabled = false));
  }
}
function reconciliationSummary(r) {
  const reviewed =
    r.rejectedRecordCount || r.skippedRecordCount || !r.sourceAmountComplete;
  return `<div class="reconciliation-head">${badge(r.status)}<span>${number(r.sourceRecordCount)} source rows</span></div><div class="reconciliation-totals"><div><span>Source amount ${!r.sourceAmountComplete ? "(known only)" : ""}</span><strong>${money(r.totalSourceAmount)}</strong></div><span class="equation">−</span><div><span>Newly imported amount</span><strong>${money(r.totalImportedAmount)}</strong></div><span class="equation">=</span><div class="${r.difference ? "text-amber" : "text-teal"}"><span>Difference (CAD)</span><strong>${money(r.difference)}</strong></div></div><div class="row-counts"><span><i class="legend-dot"></i><strong>${r.importedRecordCount}</strong> accepted</span><span><i class="legend-dot red"></i><strong>${r.rejectedRecordCount}</strong> rejected</span><span><i class="legend-dot amber"></i><strong>${r.skippedRecordCount}</strong> skipped</span></div><div class="reconciliation-explanation ${reviewed ? "review" : ""}">${!r.sourceAmountComplete ? "Source total is incomplete: some amounts are unparseable or source rows were not fully processed. The displayed difference covers known amounts only. " : r.status === "RECONCILED" ? "Source and newly imported totals match. All source rows were imported." : "Review rejected and skipped rows before signing off this batch. "} ${r.skippedRecordCount ? "Skipped duplicates did not create new records or add to the imported amount." : ""}</div>`;
}
async function upload(e) {
  e.preventDefault();
  const form = e.target,
    button = form.querySelector("button"),
    resultNode = $("#upload-result"),
    label = button.innerHTML;
  importKind = new FormData(form).get("kind");
  button.disabled = true;
  button.innerHTML = icon("refresh") + "Validating & importing…";
  resultNode.className = "";
  resultNode.textContent =
    "Processing the CSV. Keep this view open for the reconciliation result.";
  try {
    const result = await api("/api/imports", {
      method: "POST",
      body: new FormData(form),
    });
    await refresh();
    if (view === "imports") {
      dialog(
        "Reconciliation result",
        reconciliationSummary(result) +
          '<div class="form-actions"><button class="button" id="inspect-result">Inspect batch rows ' +
          icon("arrow") +
          "</button></div>",
        "batch-dialog",
      );
      $("#inspect-result").onclick = () => batchDialog(result.batchId);
    }
  } catch (err) {
    await refresh();
    if (view === "imports" && $("#upload-result")) {
      $("#upload-result").className = "result-error";
      $("#upload-result").textContent =
        safeMessage(err.message) +
        " Check migration batches before retrying. Correct any CSV validation errors first.";
    }
  } finally {
    button.disabled = false;
    button.innerHTML = label;
  }
}
async function batchDialog(id) {
  dialog("Batch reconciliation", skeleton(), "batch-dialog");
  const ticket = dialogSequence;
  try {
    const [r, first] = await Promise.all([
      api("/api/imports/" + encodeURIComponent(id) + "/reconciliation"),
      api(
        "/api/imports/" +
          encodeURIComponent(id) +
          "/records?page=1&pageSize=50",
      ),
    ]);
    if (ticket !== dialogSequence || !$("#modal").open) return;
    const b = state.imports.find((b) => b.id === id);
    $("#modal-body").innerHTML =
      `<div class="batch-identity"><strong>${escapeHtml(b?.fileName || "Imported CSV")}</strong><span>${escapeHtml(r.kind)} · <code>${escapeHtml(id)}</code></span></div>${reconciliationSummary(r)}<div class="row-inspection-head"><h3>Source row outcomes</h3><span id="batch-page-count"></span></div><div id="batch-rows"></div><div class="pagination"><span id="batch-row-range"></span><div><button class="button secondary small-button" id="batch-prev" aria-label="Previous source rows">←</button><span id="batch-page"></span><button class="button secondary small-button" id="batch-next" aria-label="Next source rows">→</button></div></div>`;
    let page = 1,
      pending = false,
      current = first;
    function draw(data) {
      current = data;
      const pages = Math.max(1, Math.ceil(data.total / 50));
      $("#batch-rows").innerHTML = table(
        [
          "Source row",
          "Municipality",
          "Source amount",
          "Imported amount",
          "Outcome",
          "Diagnostic",
        ],
        data.items.map(
          (row) =>
            `<tr><td class="numeric">${row.rowNumber}</td><td>${escapeHtml(row.municipalityCode || "Not supplied")}</td><td class="numeric">${row.sourceAmount === null ? "Unparseable" : money(row.sourceAmount)}</td><td class="numeric">${money(row.importedAmount)}</td><td>${badge(row.status)}</td><td><strong class="error-code">${escapeHtml(row.errorCode || "")}</strong><small>${escapeHtml(safeMessage(row.message || "Validated and committed"))}</small></td></tr>`,
        ),
        "Source row numbers refer to the CSV. Accepted means a new record was committed.",
      );
      $("#batch-page-count").textContent =
        `${data.total} persisted row outcomes`;
      $("#batch-row-range").textContent = data.total
        ? `${(page - 1) * 50 + 1}–${(page - 1) * 50 + data.items.length} of ${data.total} rows`
        : "No persisted rows";
      $("#batch-page").textContent = `Page ${page} of ${pages}`;
      $("#batch-prev").disabled = page === 1;
      $("#batch-next").disabled = page === pages;
    }
    async function move(delta) {
      if (pending) return;
      pending = true;
      $("#batch-prev").disabled = true;
      $("#batch-next").disabled = true;
      $("#batch-page").textContent = "Loading rows…";
      try {
        const data = await api(
          "/api/imports/" +
            encodeURIComponent(id) +
            "/records?" +
            new URLSearchParams({ page: page + delta, pageSize: 50 }),
        );
        if (ticket !== dialogSequence || !$("#modal").open) return;
        page += delta;
        draw(data);
      } catch (e) {
        if (ticket === dialogSequence && $("#modal").open) {
          $("#modal-result").className = "result-error";
          $("#modal-result").textContent = safeMessage(e.message);
          draw(current);
        }
      } finally {
        pending = false;
      }
    }
    draw(first);
    $("#batch-prev").onclick = () => move(-1);
    $("#batch-next").onclick = () => move(1);
  } catch (e) {
    if (ticket === dialogSequence && $("#modal").open)
      $("#modal-body").innerHTML = empty(
        "Batch data unavailable",
        escapeHtml(safeMessage(e.message)),
        "errors",
      );
  }
}
function setRange(value) {
  if (value === "custom") return;
  $("#from").value =
    value === "all"
      ? ""
      : day(new Date(Date.now() - (Number(value) - 1) * 86400000));
  $("#to").value = value === "all" ? "" : day();
}
function initialize() {
  if (!$("#range") || !$("#menu-toggle")) {
    if ($("#notice"))
      showNotice(
        "The workspace was updated. Reload the dashboard to continue.",
      );
    if ($("#refresh"))
      $("#refresh").onclick = () =>
        location.assign("/?dashboard=v2" + location.hash);
    return;
  }
  $("#close-modal").onclick = () => {
    $("#modal").close();
    dialogSequence++;
  };
  $("#modal").addEventListener("cancel", () => dialogSequence++);
  $("#refresh").onclick = refresh;
  document
    .querySelectorAll("nav [data-view]")
    .forEach((b) => (b.onclick = () => navigate(b.dataset.view)));
  $("#municipality").onchange = () => {
    Object.values(listState).forEach((s) => (s.page = 1));
    refresh();
  };
  $("#range").onchange = (e) => {
    setRange(e.target.value);
    Object.values(listState).forEach((s) => (s.page = 1));
    refresh();
  };
  for (const id of ["from", "to"])
    $("#" + id).onchange = () => {
      $("#range").value = "custom";
      refresh();
    };
  $("#reset").onclick = () => {
    $("#municipality").value = "";
    $("#range").value = "28";
    setRange("28");
    Object.values(listState).forEach((s) => {
      s.query = "";
      s.category = "";
      s.page = 1;
    });
    refresh();
  };
  $("#menu-toggle").onclick = () => {
    const open = $("#sidebar").classList.toggle("mobile-open");
    if (open) $("#sidebar nav button.active")?.focus();
    $("#menu-toggle").setAttribute("aria-expanded", String(open));
  };
  function closeNavigation() {
    $("#sidebar").classList.remove("mobile-open");
    $("#menu-toggle").setAttribute("aria-expanded", "false");
    $("#menu-toggle").focus();
  }
  $("#close-navigation").onclick = closeNavigation;
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && $("#sidebar").classList.contains("mobile-open"))
      closeNavigation();
  });
  $("#access").onclick = () => {
    dialog(
      "API access",
      '<p class="muted">The key stays in memory for this browser tab. Local demo mode does not require a key.</p><form id="key-form" class="form-grid"><label class="wide">X-Api-Key<input name="key" type="password" autocomplete="off"></label><div class="wide form-actions"><button class="button">Connect</button></div></form>',
    );
    $("#key-form").onsubmit = (e) => {
      e.preventDefault();
      apiKey = new FormData(e.target).get("key");
      $("#modal").close();
      dialogSequence++;
      refresh();
    };
  };
  window.addEventListener("hashchange", () => {
    if (location.hash === "#main") return;
    view = names[location.hash.slice(1)] ? location.hash.slice(1) : "overview";
    render();
  });
  view = names[location.hash.slice(1)] ? location.hash.slice(1) : "overview";
  setRange("28");
  render();
  refresh();
}
initialize();
