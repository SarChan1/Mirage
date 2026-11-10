import { test } from "node:test";
import assert from "node:assert/strict";
import { clock, duration, finishTime, validMinutes } from "../src/format.mjs";
test("countdown rounds remaining time up without displaying negatives", () => {
  assert.equal(clock(9.2), "00:10");
  assert.equal(clock(-3), "00:00");
});
test("clock crosses minute and hour boundaries", () => {
  assert.equal(clock(60), "01:00");
  assert.equal(clock(3661), "01:01:01");
});
test("duration keeps the full total instead of wrapping hours", () => {
  assert.equal(duration(16), "16 min");
  assert.equal(duration(120), "2h");
  assert.equal(duration(1440), "24h");
});
test("only valid integer durations are accepted", () => {
  for (const value of ["0", "-1", "16.5", "abc", "1441", ""])
    assert.equal(validMinutes(value), false);
  for (const value of ["1", "16", "1440"])
    assert.equal(validMinutes(value), true);
});
test("finish estimate includes the date when the sequence crosses midnight", () => {
  const start = new Date(2026, 10, 2, 23, 50).getTime();
  assert.match(finishTime(16 * 60, start), /03\/11/);
  assert.match(finishTime(16 * 60, start), /00:06/);
});
test("finish estimate keeps the current day compact and rejects invalid times", () => {
  assert.equal(
    finishTime(16 * 60, new Date(2026, 10, 2, 12, 0).getTime()),
    "12:16",
  );
  for (const seconds of [0, -1, NaN, Infinity])
    assert.equal(finishTime(seconds), "—");
});
