---
name: licensing-integrate
description: Integrate Node.aec licensing into a partner's Autodesk Revit plugin (new plugin from the template, existing plugin, or learning from the Sample Plugin). Trigger whenever asked to "integrate node.aec licensing", "add license check", "protect revit command", "setup node.aec licensing", "migrate a plugin to node.aec", or to explain how a partner plugin uses Gate.Validate. Not for the Connector's own lease sync, sign-in or storage code.
---

# Licensing integrate (pointer)

The canonical version of this skill lives in the Sample Plugin, where partners find and
copy it:

**<https://github.com/nodeaec/revit-sample-plugin/tree/main/.agents/skills/licensing-integrate>**

Load that `SKILL.md` (and the `references/` it points to) and follow it. When a local
checkout exists next to this repo, read `../revit-sample-plugin/.agents/skills/licensing-integrate/`.

Do not copy its content here: a second copy goes stale. Partner-facing changes go to the
sample's skill, and from there to the template's copy
(`templates-revit/content/__PluginName__/.agents/skills/licensing-integrate/`).

What this repo owns for partners is the human guide
[`docs/PARTNER_INTEGRATION.md`](../../../docs/PARTNER_INTEGRATION.md). Keep the two
consistent: the guide for people, the skill for agents.
