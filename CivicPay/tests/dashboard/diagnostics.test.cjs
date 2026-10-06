// Pure presentation helpers: run with node --test tests/dashboard/diagnostics.test.cjs.
// Browser workflows are verified separately against the running API.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const source = fs.readFileSync(
  path.join(__dirname, "../../dashboard/app.js"),
  "utf8",
);
const helpers = vm.runInNewContext(
  source.slice(
    source.indexOf("const escapeHtml"),
    source.indexOf("const money"),
  ) +
    source.slice(
      source.indexOf("function safeMessage"),
      source.indexOf("function diagnostic"),
    ) +
    "({escapeHtml, safeMessage})",
);
test("diagnostics redact quoted, JSON and unquoted credential values", () => {
  const input =
    'password="municipal secret"; apiKey=abc123; "access_token": "private value"; pwd=hidden';
  const safe = helpers.safeMessage(input);
  for (const secret of ["municipal", "secret", "abc123", "private", "hidden"])
    assert.ok(!safe.includes(secret));
  assert.ok(safe.includes("[redacted]"));
});
test("diagnostics hide bearer tokens and connection details", () => {
  const safe = helpers.safeMessage(
    "Authorization: Bearer eyJ.private.token\nData Source=private-host;User ID=admin;Password=credentials",
  );
  for (const secret of ["eyJ", "private", "admin", "credentials"])
    assert.ok(!safe.includes(secret));
});
test("diagnostics remove .NET exception headers and stack frames while preserving public text", () => {
  const safe = helpers.safeMessage(
    "Request could not be completed.\nMicrosoft.Data.SqlClient.SqlException: internal details\n   at CivicPay.Service.Save() in /private/internal.cs:line 40\n--- End of stack trace ---",
  );
  assert.equal(safe, "Request could not be completed.");
  assert.equal(helpers.safeMessage("x".repeat(900)).length, 600);
});
test("untrusted diagnostic text cannot inject HTML through the text escaper", () => {
  const safe = helpers.escapeHtml(
    '<img src="x" onerror="alert(1)"> & \'quoted\'',
  );
  assert.ok(!safe.includes("<") && !safe.includes(">"));
  assert.ok(
    safe.includes("&lt;img") &&
      safe.includes("&quot;") &&
      safe.includes("&#39;"),
  );
});
