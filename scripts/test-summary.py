#!/usr/bin/env python3
"""Turn .trx test result files into a Markdown report (used for the GitHub Actions run summary).

Usage: python3 scripts/test-summary.py TestResults/*.trx >> "$GITHUB_STEP_SUMMARY"

Output: a table per test project, any failures with their error message, and every test
grouped by class (collapsed) with its outcome and duration. Exits 1 if a test failed.
"""
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
ICON = {"Passed": "✅", "Failed": "❌", "NotExecuted": "⏭️"}


def seconds(duration: str) -> float:
    """'00:00:01.2345678' -> 1.2345678"""
    hours, minutes, secs = duration.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(secs)


def fmt(secs: float) -> str:
    return f"{secs * 1000:.0f} ms" if secs < 1 else f"{secs:.2f} s"


def read_trx(path: Path):
    root = ET.parse(path).getroot()
    class_of = {
        test.get("id"): test.find("t:TestMethod", NS).get("className")
        for test in root.iterfind("t:TestDefinitions/t:UnitTest", NS)
    }
    tests = []
    for result in root.iterfind("t:Results/t:UnitTestResult", NS):
        class_name = class_of.get(result.get("testId"), "")
        name = result.get("testName", "")
        if class_name and name.startswith(class_name + "."):
            name = name[len(class_name) + 1:]
        message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS)
        tests.append({
            "class": class_name.rsplit(".", 1)[-1],
            "name": name,
            "outcome": result.get("outcome", ""),
            "seconds": seconds(result.get("duration", "00:00:00")),
            "message": message.strip(),
        })
    return tests


def main(paths):
    projects = {Path(p).stem: read_trx(Path(p)) for p in sorted(paths)}
    if not projects:
        print("## 🧪 Test results\n\nNo .trx files found.")
        return 1

    def count(tests, outcome):
        return sum(t["outcome"] == outcome for t in tests)

    all_tests = [t for tests in projects.values() for t in tests]
    failed_total = count(all_tests, "Failed")
    verdict = "✅ all passed" if failed_total == 0 else f"❌ {failed_total} failed"

    out = [f"## 🧪 Test results: {len(all_tests)} tests, {verdict}", ""]
    out += ["| Project | Total | ✅ Passed | ❌ Failed | ⏭️ Skipped | Duration |",
            "|---|---:|---:|---:|---:|---:|"]
    for project, tests in projects.items():
        out.append(f"| {project} | {len(tests)} | {count(tests, 'Passed')} | {count(tests, 'Failed')} "
                   f"| {count(tests, 'NotExecuted')} | {fmt(sum(t['seconds'] for t in tests))} |")
    out.append("")

    failures = [(p, t) for p, tests in projects.items() for t in tests if t["outcome"] == "Failed"]
    if failures:
        out += ["### ❌ Failed tests", ""]
        for project, t in failures:
            out += [f"**{project} › {t['class']} › {t['name']}**", "```", t["message"] or "(no message)", "```", ""]

    out += ["### All tests by project and class", ""]
    for project, tests in projects.items():
        by_class = defaultdict(list)
        for t in tests:
            by_class[t["class"]].append(t)
        out += [f"<details><summary><b>{project}</b> ({len(tests)} tests)</summary>", ""]
        for class_name in sorted(by_class):
            group = by_class[class_name]
            ok = count(group, "Passed")
            out += [f"**{class_name}**: {ok}/{len(group)} passed", "",
                    "| | Test | Duration |", "|---|---|---:|"]
            for t in sorted(group, key=lambda x: x["name"]):
                name = t["name"].replace("|", "\\|")
                out.append(f"| {ICON.get(t['outcome'], '❔')} | {name} | {fmt(t['seconds'])} |")
            out.append("")
        out += ["</details>", ""]

    print("\n".join(out))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
