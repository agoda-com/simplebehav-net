import assert from "node:assert/strict";
import { mkdtemp, readdir } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import {
  type Assertion,
  type Constructable,
  type SetupContext,
  type SystemUnderTestBuilder,
  Spec,
  setup,
  slug,
} from "../src/index.ts";

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
});

test("long titles do not share a directory", () => {
  const method = "UserFilledAllPropertyDetailsUpdatesThemProceedToTheNextStepAndValidatePayload";
  const mobile = slug(`${method}[chromium-mobile ${method}]`);
  const desktop = slug(`${method}[chromium-desktop ${method}]`);
  assert.notEqual(mobile, desktop);
  assert.ok(mobile.length <= 80);
});

async function findBundles(root: string): Promise<string[]> {
  const found: string[] = [];
  async function walk(dir: string): Promise<void> {
    for (const entry of await readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) await walk(full);
      else if (entry.name === "bundle.json") found.push(full);
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
