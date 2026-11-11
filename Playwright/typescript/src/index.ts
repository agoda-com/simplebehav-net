import { access } from "node:fs/promises";
import { cp, mkdir } from "node:fs/promises";
import { createRequire } from "node:module";
import path from "node:path";
import { chromium, type Browser, type Page, type Route } from "playwright";
import {
  type Assertion,
  type Constructable,
  type ScenarioTrace,
  type Setup,
  type SetupContext,
  type SystemUnderTestBuilder,
  Spec,
} from "simplebehav";

const require = createRequire(import.meta.url);

export class PlaywrightSetupContext implements SetupContext {
  private readonly routes: Array<[string, (route: Route) => Promise<void> | void]> = [];
  private readonly scripts: string[] = [];

  constructor(readonly page: Page) {}

  mockRoute(pattern: string, handler: (route: Route) => Promise<void> | void): void {
    this.routes.push([pattern, handler]);
  }

  addInitScript(script: string): void {
    this.scripts.push(script);
  }

  async applyToPage(): Promise<void> {
    for (const script of this.scripts) await this.page.addInitScript(script);
    for (const [pattern, handler] of this.routes) await this.page.route(pattern, handler);
  }
}

export interface PlaywrightSetup extends Setup {
  apply(context: PlaywrightSetupContext): void | Promise<void>;
}

export class PlaywrightBuilder implements SystemUnderTestBuilder, ScenarioTrace {
  constructor(private readonly page: Page) {}

  async initialize(): Promise<void> {}

  createContext(): PlaywrightSetupContext {
    return new PlaywrightSetupContext(this.page);
  }

  async apply(context: SetupContext, item: Setup): Promise<void> {
    if (!(context instanceof PlaywrightSetupContext)) {
      throw new Error("PlaywrightBuilder requires PlaywrightSetupContext");
    }
    if (!isPlaywrightSetup(item)) {
      throw new Error(`PlaywrightBuilder cannot apply ${item.label}. Implement PlaywrightSetup.`);
    }
    await item.apply(context);
    await context.applyToPage();
  }

  construct<T>(context: SetupContext, type: Constructable<T>): T {
    return new (type as new (page: Page) => T)((context as PlaywrightSetupContext).page);
  }

  async dispose(): Promise<void> {}

  async startTrace(): Promise<void> {
    await this.page.context().tracing.start({ screenshots: true, snapshots: true, sources: false });
  }

  async stopTrace(tracePath: string): Promise<void> {
    await mkdir(path.dirname(tracePath), { recursive: true });
    await this.page.context().tracing.stop({ path: tracePath });
  }

  async ensureViewer(artifactsRoot: string): Promise<void> {
    const destination = path.join(artifactsRoot, "trace-viewer");
    try {
      await access(path.join(destination, "index.html"));
      return;
    } catch {
      // copied on the first scenario
    }
    const source = path.join(path.dirname(require.resolve("playwright-core/package.json")), "lib", "vite", "traceViewer");
    await cp(source, destination, { recursive: true });
  }
}

function isPlaywrightSetup(item: Setup): item is PlaywrightSetup {
  return "apply" in item && typeof item.apply === "function";
}

export const See = {
  textVisible(partialText: string, timeoutMs = 10_000): Assertion {
    return pageAssertion(`"${partialText}" visible on page`, async (page) => {
      await page.getByText(partialText, { exact: false }).waitFor({ state: "visible", timeout: timeoutMs });
    });
  },

  elementVisible(selector: string, timeoutMs = 10_000): Assertion {
    return pageAssertion(`element "${selector}" visible`, async (page) => {
      await page.locator(selector).waitFor({ state: "visible", timeout: timeoutMs });
    });
  },

  buttonDisabled(testId: string, timeoutMs = 5_000): Assertion {
    return pageAssertion(`button [${testId}] is disabled`, async (page) => {
      const button = page.getByTestId(testId);
      await button.waitFor({ state: "visible", timeout: timeoutMs });
      if (!(await button.isDisabled())) throw new Error(`Expected button [${testId}] to be disabled but it was enabled`);
    });
  },
};

function pageAssertion(label: string, check: (page: Page) => Promise<void>): Assertion {
  return {
    label,
    async run(context) {
      if (!(context instanceof PlaywrightSetupContext)) throw new Error("See assertions require a Playwright setup context");
      await check(context.page);
    },
  };
}

export async function openBrowser(): Promise<Browser> {
  const headed = (process.env.SIMPLEBEHAV_HEADLESS ?? "").toLowerCase() === "false";
  return chromium.launch({ headless: !headed });
}

export function configurePage(page: Page, projectName: string, suite: string[]): void {
  Spec.configure(new PlaywrightBuilder(page), process.env.SIMPLEBEHAV_ARTIFACTS ?? "artifacts", projectName);
  Spec.suite = suite;
}
