import assert from "node:assert/strict";
import { mkdtemp, readdir } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { readFile } from "node:fs/promises";
import {
  type Assertion,
  type Constructable,
  type SetupContext,
  type SystemUnderTestBuilder,
  Spec,
  setup,
  slug,
} from "../src/index.ts";
import { generateSuitePage } from "../src/suiteReport.ts";

class Widget {
  readonly name = "widget";
}

test("scenario records Given When Then and writes bundle.json", async () => {
  const out = await mkdtemp(path.join(tmpdir(), "simplebehav-"));
  const adapter = new RecordingBuilder();
  Spec.configure(adapter, out, "sample");
  Spec.testName = "AddsItem";
  Spec.suite = ["sample", "Scenario"];
  Spec.configureScenario = undefined;

  await Spec.scenario()
    .given(setup("catalog is stocked"))
    .when("user adds the item", Widget, (widget) => {
      adapter.seen = widget.name;
    })
    .then("cart shows the item", {
      label: "cart shows the item",
      async run() {
        assert.equal(adapter.seen, "widget");
      },
    } satisfies Assertion)
    .run();

  assert.deepEqual(adapter.applied, ["catalog is stocked"]);
  const bundles = await findBundles(out);
  assert.equal(bundles.length, 1);
  const pages = await findNamed(out, "index.html");
  assert.equal(pages.length, 1);
  const html = await readFile(pages[0], "utf8");
  assert.match(html, /AddsItem/);
  assert.match(html, /ALL PASSED/);
  assert.match(html, /Given catalog is stocked/);
  assert.match(html, /When user adds the item/);
  assert.match(html, /Then cart shows the item/);
});

test("a failed scenario still writes the suite page", async () => {
  const out = await mkdtemp(path.join(tmpdir(), "simplebehav-"));
  Spec.configure(new RecordingBuilder(), out, "sample");
  Spec.testName = "RejectsItem";
  Spec.suite = ["sample", "Scenario"];
  Spec.configureScenario = undefined;

  await assert.rejects(
    () => Spec.scenario()
      .when("user adds the item", Widget, () => undefined)
      .then("cart shows the item", {
        label: "cart shows the item",
        async run() {
          throw new Error("timeout");
        },
      } satisfies Assertion)
      .run(),
    /RejectsItem/,
  );

  const pages = await findNamed(out, "index.html");
  const html = await readFile(pages[0], "utf8");
  assert.match(html, /1 FAILED/);
  assert.match(html, /pre-error/);
  assert.match(html, /data-title="RejectsItem" open/);
});

test("suite page lists scenarios and points at the trace viewer", () => {
  const html = generateSuitePage("CheckoutTests", [
    {
      slug: "user-places-order",
      bundle: {
        id: "1",
        title: "user places order",
        status: "pass",
        startedAt: "2026-11-09T00:00:00.000Z",
        durationMs: 1234,
        steps: [
          { type: "Setup", label: "checkout page is available", durationMs: 4 },
          { type: "Assert", label: "toast visible", durationMs: 300 },
        ],
      },
    },
    {
      slug: "user-clears-card",
      bundle: {
        id: "2",
        title: "user clears card",
        status: "fail",
        startedAt: "2026-11-09T00:00:01.000Z",
        durationMs: 40,
        steps: [{ type: "Assert", label: "FAILED: timeout", durationMs: 40, error: "timeout" }],
      },
    },
  ], "../../trace-viewer/index.html");

  assert.match(html, /CheckoutTests/);
  assert.match(html, /Given checkout page is available/);
  assert.match(html, /1 FAILED/);
  assert.match(html, /"\.\.\/\.\.\/trace-viewer\/index\.html"/);
  assert.match(html, /data-title="user clears card" open/);
  assert.equal(html.includes("data:image/png;base64"), false);
});

test("long titles do not share a directory", () => {
  const method = "UserFilledAllPropertyDetailsUpdatesThemProceedToTheNextStepAndValidatePayload";
  const mobile = slug(`${method}[chromium-mobile ${method}]`);
  const desktop = slug(`${method}[chromium-desktop ${method}]`);
  assert.notEqual(mobile, desktop);
  assert.ok(mobile.length <= 80);
});

async function findBundles(root: string): Promise<string[]> {
  return findNamed(root, "bundle.json");
}

async function findNamed(root: string, name: string): Promise<string[]> {
  const found: string[] = [];
  async function walk(dir: string): Promise<void> {
    for (const entry of await readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) await walk(full);
      else if (entry.name === name) found.push(full);
    }
  }
  await walk(root);
  return found;
}

class RecordingBuilder implements SystemUnderTestBuilder {
  readonly applied: string[] = [];
  seen: string | undefined;
  private readonly context: SetupContext = {};

  async initialize(): Promise<void> {}
  createContext(): SetupContext {
    return this.context;
  }
  async apply(_context: SetupContext, item: { label: string }): Promise<void> {
    this.applied.push(item.label);
  }
  construct<T>(_context: SetupContext, type: Constructable<T>): T {
    return new type();
  }
  async dispose(): Promise<void> {}
}
