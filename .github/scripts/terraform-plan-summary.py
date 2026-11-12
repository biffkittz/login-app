#!/usr/bin/env python3
"""Summarize a Terraform plan (the output of `terraform show -json`) as Markdown that is safe to publish.

The repository is public, and so are its workflow logs, job summaries and pull request comments. A plan's JSON holds
every attribute value in clear text, sensitive ones included (`before_sensitive` and `after_sensitive` only flag
them), so this summary never prints a value. It lists each resource that changes, how, and the names of the
top-level attributes that change. The one exception is the image a Container App runs, which is what a release
changes and what a reviewer most needs to see. It is printed only when Terraform doesn't mark it sensitive.

    terraform-plan-summary.py --mode deploy plan.json > summary.md
"""

from __future__ import annotations

import argparse
import json
import sys
from typing import Any

MAX_ROWS = 150

ACTIONS = {
    ("create",): "create",
    ("update",): "update",
    ("delete",): "**destroy**",
    ("delete", "create"): "**replace**",
    ("create", "delete"): "**replace** (new one first)",
    ("read",): "read",
}

HEADINGS = {
    "pr": "Terraform plan for this pull request",
    "deploy": "Terraform plan for main",
    "drift": "Drift check: main against the real infrastructure",
}

NOTES = {
    "pr": (
        "Speculative plan without a refresh: it compares the configuration with the state, so changes made outside "
        "Terraform don't show here. Nothing is applied from a pull request; merging to main plans again and asks for "
        "approval before applying."
    ),
    "deploy": (
        "Approve the **demo** deployment to apply exactly this plan. Attribute values aren't shown because the "
        "repository is public; to read them, decrypt the plan artifact (docs/CI_CD.md, Reviewing a plan in detail)."
    ),
    "drift": (
        "Any change listed here means the infrastructure no longer matches main: someone changed it outside "
        "Terraform, or a provider sees it differently. Run the infra workflow on main to plan and apply, or fix the "
        "code."
    ),
}


def contains_true(value: Any) -> bool:
    """Tell whether a JSON value is `true` or holds `true` anywhere (an after_unknown or *_sensitive marker)."""
    if value is True:
        return True
    if isinstance(value, dict):
        return any(contains_true(item) for item in value.values())
    if isinstance(value, list):
        return any(contains_true(item) for item in value)
    return False


def changed_attributes(change: dict[str, Any]) -> list[str]:
    """Names of the top-level attributes whose value changes, or becomes known only after apply."""
    before = change.get("before") or {}
    after = change.get("after") or {}
    unknown = change.get("after_unknown") or {}
    if not isinstance(before, dict) or not isinstance(after, dict):
        return []
    names = set(before) | set(after) | (set(unknown) if isinstance(unknown, dict) else set())
    changed = []
    for name in sorted(names):
        becomes_unknown = isinstance(unknown, dict) and contains_true(unknown.get(name))
        if before.get(name) != after.get(name) or becomes_unknown:
            changed.append(name)
    return changed


def forcing_attributes(change: dict[str, Any]) -> set[str]:
    """Top-level attributes whose change forces the resource to be replaced."""
    return {path[0] for path in change.get("replace_paths") or [] if path and isinstance(path[0], str)}


def images(state: Any, sensitive: Any) -> list[str]:
    """The images of a Container App's containers, unless Terraform marks them sensitive."""
    if not isinstance(state, dict):
        return []
    found = []
    templates = state.get("template") or []
    sensitive_templates = sensitive.get("template") if isinstance(sensitive, dict) else None
    for index, template in enumerate(templates):
        sensitive_template = sensitive_templates[index] if isinstance(sensitive_templates, list) and index < len(sensitive_templates) else None
        if sensitive_template is True:
            continue
        sensitive_containers = sensitive_template.get("container") if isinstance(sensitive_template, dict) else None
        for position, container in enumerate(template.get("container") or []):
            marker = sensitive_containers[position] if isinstance(sensitive_containers, list) and position < len(sensitive_containers) else None
            if marker is True or (isinstance(marker, dict) and marker.get("image") is True):
                continue
            image = container.get("image")
            if isinstance(image, str):
                found.append(image)
    return found


def cell(text: str) -> str:
    """Escape text for a Markdown table cell."""
    return text.replace("|", "\\|").replace("\n", " ")


def summarize(plan: dict[str, Any], mode: str) -> tuple[str, int]:
    rows = []
    image_changes = []
    counts = {"create": 0, "update": 0, "replace": 0, "destroy": 0}

    for resource in plan.get("resource_changes") or []:
        change = resource.get("change") or {}
        actions = tuple(change.get("actions") or [])
        if actions in (("no-op",), ("read",)) or not actions:
            continue
        label = ACTIONS.get(actions, "/".join(actions))
        if "delete" in actions and "create" in actions:
            counts["replace"] += 1
        elif "delete" in actions:
            counts["destroy"] += 1
        elif "create" in actions:
            counts["create"] += 1
        else:
            counts["update"] += 1

        forcing = forcing_attributes(change)
        attributes = [f"{name} (forces replacement)" if name in forcing else name for name in changed_attributes(change)]
        if "delete" in actions and "create" not in actions:
            attributes = []
        rows.append((resource.get("address", "?"), label, ", ".join(attributes)))

        if resource.get("type") == "azurerm_container_app":
            before = images(change.get("before"), change.get("before_sensitive"))
            after = images(change.get("after"), change.get("after_sensitive"))
            if before != after:
                image_changes.append((resource.get("address", "?"), ", ".join(before) or "none", ", ".join(after) or "none"))

    drift = [
        (item.get("address", "?"), "/".join((item.get("change") or {}).get("actions") or []))
        for item in plan.get("resource_drift") or []
        if tuple((item.get("change") or {}).get("actions") or []) not in (("no-op",), ())
    ]

    total = sum(counts.values())
    lines = [f"### {HEADINGS.get(mode, 'Terraform plan')}", ""]
    if total == 0:
        lines.append(
            "**No changes.** The configuration matches the state."
            if mode == "pr"
            else "**No changes.** The infrastructure matches the configuration."
        )
    else:
        lines.append(
            f"**{counts['create']} to create, {counts['update']} to update, {counts['replace']} to replace, "
            f"{counts['destroy']} to destroy.**"
        )
    if counts["destroy"] or counts["replace"]:
        lines += ["", "> [!WARNING]", "> This plan destroys or replaces resources. Check each one below before approving."]

    if image_changes:
        lines += ["", "**Container images**", ""]
        for address, before, after in image_changes:
            lines.append(f"- `{address}`: `{before}` → `{after}`")

    if rows:
        lines += ["", "| Resource | Action | Attributes that change |", "| --- | --- | --- |"]
        for address, label, attributes in rows[:MAX_ROWS]:
            lines.append(f"| `{cell(address)}` | {label} | {cell(attributes) or '–'} |")
        if len(rows) > MAX_ROWS:
            lines.append(f"| … and {len(rows) - MAX_ROWS} more | | |")

    if drift:
        lines += ["", "<details><summary>Changed outside Terraform since the last apply (refresh)</summary>", ""]
        lines += [f"- `{address}`: {action}" for address, action in drift[:MAX_ROWS]]
        lines += ["", "</details>"]

    lines += ["", NOTES.get(mode, ""), ""]
    return "\n".join(lines), total


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--mode", choices=sorted(HEADINGS), required=True)
    parser.add_argument("plan_json", help="output of terraform show -json <plan file>")
    arguments = parser.parse_args()

    with open(arguments.plan_json, encoding="utf-8") as handle:
        plan = json.load(handle)
    markdown, _ = summarize(plan, arguments.mode)
    sys.stdout.write(markdown)
    return 0


if __name__ == "__main__":
    sys.exit(main())
