# Agent skills

The skills live at [`.agents/skills/`](../../.agents/skills), the universal Agent Skills directory. Each skill is a folder with a `SKILL.md` (YAML frontmatter plus instructions), following the [Agent Skills specification](https://agentskills.io/specification).

| Skill | What it does |
|---|---|
| `specs-to-tests` | Flow notes → declarative SimpleBehav C# stubs |
| `imperative-to-gherkin` | Flow notes → declarative Gherkin |

They ship inside the `SimpleBehav` NuGet package. Restore does not copy them into the consuming repo. The package's build target does that on the next build, into the repo's `.agents/skills/`.

If `.agents/skills/<name>/SKILL.md` is already there, that file stays and the agent follows it. The package refreshes a skill only when it installed that copy and the files still match the stamp it wrote. An edit, or a skill this package did not install, is not replaced. An installed copy is marked `simplebehav-skill-source: nuget:SimpleBehav` so the loaded skill shows where it came from. Set `EnableEmbeddedAgentSkills` to `false` to skip the copy.
