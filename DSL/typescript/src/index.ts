import { createHash, randomUUID } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";

export type StepType = "Setup" | "Act" | "Assert";

export interface ArtifactStep {
  type: StepType;
  label: string;
  durationMs: number;
  error?: string;
}

export interface ArtifactBundle {
  id: string;
  title: string;
  status: string;
  startedAt: string;
  durationMs: number;
  steps: ArtifactStep[];
}

export interface Setup {
  label: string;
}

export interface SetupContext {}

export type Constructable<T> = new (...args: any[]) => T;

export interface SystemUnderTestBuilder {
  initialize(): Promise<void>;
  createContext(): SetupContext;
  apply(context: SetupContext, setup: Setup): Promise<void>;
  construct<T>(context: SetupContext, type: Constructable<T>): T;
  dispose(): Promise<void>;
}

export interface ScenarioTrace {
  startTrace(): Promise<void>;
  stopTrace(tracePath: string): Promise<void>;
  ensureViewer(artifactsRoot: string): Promise<void>;
}

export interface Assertion {
  label: string;
  run(context: SetupContext): Promise<void>;
}

export function setup(label: string, _action?: (context: SetupContext) => Promise<void> | void): Setup {
  return { label };
}

export function slug(value: string): string {
  const cleaned = value.toLowerCase().replaceAll(" ", "-").replace(/[^a-z0-9-]/g, "");
  if (cleaned.length <= 80) return cleaned;
  const hash = createHash("sha256").update(value).digest("hex").slice(0, 8);
  return `${cleaned.slice(0, 71)}-${hash}`;
}

function segment(value: string): string {
  const cleaned = value.replace(/[^A-Za-z0-9._-]/g, "-");
  return cleaned.length === 0 ? "suite" : cleaned;
}

export function suitePath(projectName: string, testFile?: string): string[] {
  const project = segment(projectName);
  if (!testFile) return [project];
  const parsed = path.parse(testFile);
  const folders = parsed.dir.split(/[/\\]/).filter((part) => part.length > 0).map(segment);
  return [project, ...folders, segment(parsed.name)];
}

export class GivenToken {
  constructor(private readonly runner: ScenarioRunner) {}

  given(item: Setup): GivenToken {
    return this.and(item);
  }

  and(item: Setup): GivenToken {
    this.runner.addSetup(item);
    return this;
  }

  arrange(item: Setup): GivenToken {
    return this.and(item);
  }

  when<T>(label: string, type: Constructable<T>, action: (sut: T) => Promise<void> | void): WhenToken {
    this.runner.setWhen(label, async (context) => {
      await action(this.runner.adapter.construct(context, type));
    });
    return new WhenToken(this.runner);
  }
}

export class WhenToken {
  constructor(private readonly runner: ScenarioRunner) {}

  then(label: string, assertion: Assertion): ThenToken {
    return new ThenToken(this.runner, label, assertion);
  }
}

export class ThenToken {
  private readonly assertions: Array<[string, Assertion]>;

  constructor(private readonly runner: ScenarioRunner, label: string, assertion: Assertion) {
    this.assertions = [[label, assertion]];
  }

  and(label: string, assertion: Assertion): ThenToken {
    this.assertions.push([label, assertion]);
    return this;
  }

  run(): Promise<void> {
    return this.runner.execute(this.assertions);
  }
}

class ScenarioRunner {
  readonly adapter: SystemUnderTestBuilder;
  private readonly setups: Setup[] = [];
  private whenLabel = "";
  private whenFn: ((context: SetupContext) => Promise<void>) | undefined;

  constructor(
    private readonly outDir: string,
    private readonly suite: string[],
    private readonly scenarioName: string,
    adapter: SystemUnderTestBuilder,
  ) {
    this.adapter = adapter;
  }

  addSetup(item: Setup): void {
    this.setups.push(item);
  }

  setWhen(label: string, fn: (context: SetupContext) => Promise<void>): void {
    this.whenLabel = label;
    this.whenFn = fn;
  }

  async execute(assertions: Array<[string, Assertion]>): Promise<void> {
    const emitter = new ArtifactEmitter(this.scenarioName);
    let status = "pass";
    let failureMessage: string | undefined;
    const context = this.adapter.createContext();
    const trace = isTrace(this.adapter) ? this.adapter : undefined;
    await trace?.startTrace();
    try {
      await this.runGiven(context, emitter);
      await this.runWhen(context, emitter);
      await this.runThen(context, assertions, emitter);
    } catch (error) {
      status = "fail";
      failureMessage = error instanceof Error ? error.message : String(error);
      emitter.add({ type: "Assert", label: `FAILED: ${failureMessage}`, durationMs: 0, error: failureMessage });
    }

    const bundle = emitter.build(status);
    const dir = await emitter.emit(bundle, this.outDir, this.suite);
    await trace?.stopTrace(path.join(dir, "trace.zip"));
    await trace?.ensureViewer(this.outDir);
    if (status === "fail") throw new Error(`[simplebehav] "${this.scenarioName}" failed: ${failureMessage}`);
  }

  private async runGiven(context: SetupContext, emitter: ArtifactEmitter): Promise<void> {
    if (this.setups.length === 0) return;
    const started = Date.now();
    for (const item of this.setups) await this.adapter.apply(context, item);
    const label = this.setups.map((item) => item.label).filter((item) => item.length > 0).join(", ");
    emitter.add({ type: "Setup", label: label.length > 0 ? label : "preconditions applied", durationMs: Date.now() - started });
  }

  private async runWhen(context: SetupContext, emitter: ArtifactEmitter): Promise<void> {
    if (!this.whenFn) return;
    const started = Date.now();
    await this.whenFn(context);
    emitter.add({ type: "Act", label: this.whenLabel, durationMs: Date.now() - started });
  }

  private async runThen(context: SetupContext, assertions: Array<[string, Assertion]>, emitter: ArtifactEmitter): Promise<void> {
    for (const [label, assertion] of assertions) {
      const started = Date.now();
      await assertion.run(context);
      emitter.add({ type: "Assert", label, durationMs: Date.now() - started });
    }
  }
}

function isTrace(adapter: SystemUnderTestBuilder): adapter is SystemUnderTestBuilder & ScenarioTrace {
  return "startTrace" in adapter && "stopTrace" in adapter && "ensureViewer" in adapter;
}

class ArtifactEmitter {
  private readonly id = randomUUID();
  private readonly startedAt = Date.now();
  private readonly steps: ArtifactStep[] = [];

  constructor(private readonly title: string) {}

  add(step: ArtifactStep): void {
    this.steps.push(step);
  }

  build(status: string): ArtifactBundle {
    return {
      id: this.id,
      title: this.title,
      status,
      startedAt: new Date(this.startedAt).toISOString(),
      durationMs: Date.now() - this.startedAt,
      steps: [...this.steps],
    };
  }

  async emit(bundle: ArtifactBundle, outDir: string, suite: string[]): Promise<string> {
    const dir = path.join(outDir, ...suite, slug(bundle.title));
    await mkdir(dir, { recursive: true });
    await writeFile(path.join(dir, "bundle.json"), `${JSON.stringify(bundle, null, 2)}\n`);
    await writeFile(path.join(dir, "cucumber.json"), `${JSON.stringify(cucumber(bundle, suite, slug(bundle.title)), null, 2)}\n`);
    return dir;
  }
}

function cucumber(bundle: ArtifactBundle, suite: string[], scenarioSlug: string) {
  return [
    {
      uri: `${suite.join("/")}/${scenarioSlug}`,
      id: bundle.id,
      keyword: "Feature",
      name: suite[suite.length - 1],
      line: 1,
      description: "Generated by SimpleBehav",
      elements: [
        {
          id: `${bundle.id};${scenarioSlug}`,
          keyword: "Scenario",
          name: bundle.title,
          line: 1,
          type: "scenario",
          steps: bundle.steps.map((step, index) => ({
            keyword: step.type === "Setup" ? "Given " : step.type === "Act" ? "When " : "Then ",
            name: step.label,
            line: index + 1,
            result: {
              duration: step.durationMs * 1_000_000,
              status: step.error === undefined ? "passed" : "failed",
              ...(step.error === undefined ? {} : { error_message: step.error }),
            },
          })),
        },
      ],
    },
  ];
}

class SpecState {
  resolveVariant: (() => string | undefined) | undefined;
  configureScenario: ((token: GivenToken) => GivenToken) | undefined;
  testName: string | undefined;
  projectName = "tests";
  suite: string[] | undefined;
  readonly baseUrl = process.env.SIMPLEBEHAV_BASE_URL ?? "http://localhost:5173";
  adapter: SystemUnderTestBuilder | undefined;
  outDir: string | undefined;

  configure(builder: SystemUnderTestBuilder, outDir: string, projectName?: string): void {
    this.adapter = builder;
    this.outDir = outDir;
    if (projectName && projectName.trim().length > 0) this.projectName = projectName;
  }

  scenario(name = this.testName): GivenToken {
    if (!name) throw new Error("[simplebehav] scenario name is missing");
    if (!this.adapter || !this.outDir) throw new Error("[simplebehav] Call Spec.configure(builder, outDir) before scenario().");
    const variant = this.resolveVariant?.();
    const scenarioName = variant === undefined ? name : `${name}[${variant}]`;
    const suite = this.suite ?? [segment(this.projectName)];
    const token = new GivenToken(new ScenarioRunner(this.outDir, suite, scenarioName, this.adapter));
    return this.configureScenario ? this.configureScenario(token) : token;
  }
}

export const Spec = new SpecState();
