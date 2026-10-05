"""Regression checks for version/tag and workflow approval boundaries."""

import contextlib
import io
import json
from pathlib import Path
import shutil
import tempfile
import unittest

import check


class FoundationChecks(unittest.TestCase):
    def test_semver_rejects_noncanonical_numeric_components(self):
        for version in ("01.0.0", "1.02.0", "1.0.03", "1.0", "1.0.0-preview.01", "1.0.0-"):
            with self.subTest(version=version), self.assertRaises(ValueError):
                check.validate_semver(version)

    def test_semver_accepts_prerelease_and_stable_families(self):
        for version in ("1.0.0-preview.1", "1.2.3", "2.0.0-rc.0", "1.0.0+commit"):
            check.validate_semver(version)

    def test_tags_cannot_misstate_the_family_or_tool_revision(self):
        mame = {"version": "0.289", "rebuildRevision": 1}
        for tag in ("libchdr-v0.3.0", "libchdr-native-v1.0.0-preview.1", "chdman-0.289-r2", "other"):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                check.validate_tag(tag, "1.0.0-preview.1", mame)
        check.validate_tag("libchdr-v1.0.0-preview.1", "1.0.0-preview.1", mame)
        check.validate_tag("chdman-0.289-r1", "1.0.0-preview.1", mame)

    def test_source_pin_and_permission_drift_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            shutil.copytree(check.ROOT / "eng", root / "eng")
            shutil.copytree(check.ROOT / ".github", root / ".github")
            with contextlib.redirect_stdout(io.StringIO()):
                check.check(root)
            pin_path = root / "eng/pins/libchdr.json"
            original = pin_path.read_text()
            pin = json.loads(original)
            pin["commit"] = "0" * 40
            pin_path.write_text(json.dumps(pin))
            with self.assertRaisesRegex(ValueError, "commits disagree"):
                check.check(root)
            pin_path.write_text(original)
            workflow = root / ".github/workflows/release.yml"
            workflow.write_text(workflow.read_text().replace("contents: read", "contents: write"))
            with self.assertRaises(ValueError):
                check.check(root)

    def test_mutable_action_tag_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            shutil.copytree(check.ROOT / "eng", root / "eng")
            shutil.copytree(check.ROOT / ".github", root / ".github")
            workflow = root / ".github/workflows/ci.yml"
            workflow.write_text(workflow.read_text().replace(check.ACTIONS["actions/checkout"], "v7"))
            with self.assertRaisesRegex(ValueError, "unreviewed action"):
                check.check(root)

    def test_unsupported_yaml_forms_cannot_bypass_review(self):
        changes = (
            ("ci.yml", "  extra:\n    permissions: {contents: write}\n"),
            ("publish.yaml", "name: Publish\npermissions: write-all\njobs:\n  publish:\n    steps:\n      - run: gh release create x\n"),
            ("ci.yml", "      - {uses: unknown/action@main}\n"),
        )
        for name, addition in changes:
            with self.subTest(name=name, addition=addition), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                shutil.copytree(check.ROOT / "eng", root / "eng")
                shutil.copytree(check.ROOT / ".github", root / ".github")
                workflow = root / ".github/workflows" / name
                workflow.write_text((workflow.read_text() if workflow.exists() else "") + addition)
                with self.assertRaises(ValueError):
                    check.check(root)

    def test_actual_tag_trigger_cannot_disappear(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            shutil.copytree(check.ROOT / "eng", root / "eng")
            shutil.copytree(check.ROOT / ".github", root / ".github")
            workflow = root / ".github/workflows/ci.yml"
            workflow.write_text(workflow.read_text().replace("    tags: ['libchdr-*', 'chdman-*']\n", ""))
            with self.assertRaisesRegex(ValueError, "actual library/tool tag"):
                check.check(root)

    def test_secrets_cannot_be_referenced_with_alternative_expression_syntax(self):
        for expression in ("${{ secrets['PUBLISH_TOKEN'] }}", "${{ format('{0}', secrets.PUBLISH_TOKEN) }}"):
            with self.subTest(expression=expression), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                shutil.copytree(check.ROOT / "eng", root / "eng")
                shutil.copytree(check.ROOT / ".github", root / ".github")
                workflow = root / ".github/workflows/ci.yml"
                workflow.write_text(workflow.read_text().replace("CI: true", f"CI: {expression}"))
                with self.assertRaises(ValueError):
                    check.check(root)

    def test_commented_tag_routing_is_not_active_evidence(self):
        for line in ("    tags: ['libchdr-*', 'chdman-*']", "        if: github.ref_type == 'tag'",
                     "          EXPECTED_TAG: ${{ github.ref_name }}",
                     '      - run: python3 -B eng/check.py --tag "$EXPECTED_TAG"'):
            with self.subTest(line=line), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                shutil.copytree(check.ROOT / "eng", root / "eng")
                shutil.copytree(check.ROOT / ".github", root / ".github")
                workflow = root / ".github/workflows/ci.yml"
                workflow.write_text(workflow.read_text().replace(line, "#" + line))
                with self.assertRaisesRegex(ValueError, "actual library/tool tag"):
                    check.check(root)


if __name__ == "__main__":
    unittest.main()
