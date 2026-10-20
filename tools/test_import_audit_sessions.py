"""Keep automated native fixtures out of reported gameplay reach."""
import unittest

from check_import_audit_log import is_fixture, split_sessions


def event(stage, field="", subject=""):
    return {"stage": stage, "field": field, "subject": subject}


class SessionEvidenceTests(unittest.TestCase):
    def test_fixture_loading_does_not_count_as_gameplay(self):
        sessions = split_sessions([
            event("PLUGIN_LOADED"), event("MATCH_STARTED", subject="Audit 01"),
            event("PLUGIN_LOADED"), event("FIXTURE_SESSION"),
            event("TEAM_APPLIED", subject="Audit 02"),
        ])
        gameplay = [e for s in sessions if not is_fixture(s) for e in s]
        self.assertEqual([e["subject"] for e in gameplay if e["stage"] == "MATCH_STARTED"], ["Audit 01"])
        self.assertFalse(any(e["subject"] == "Audit 02" for e in gameplay))
        self.assertTrue(is_fixture(sessions[-1]))

    def test_legacy_native_composition_session_is_fixture(self):
        self.assertTrue(is_fixture([event("MATERIAL_FLAGS_CHECKED", "native-composition")]))
        self.assertTrue(is_fixture([event("MATERIAL_GROUP_CONFLICT", "fixture-render-flags")]))

    def test_live_material_and_field_checks_remain_gameplay_evidence(self):
        self.assertFalse(is_fixture([
            event("PLAYER_FIELDS_AT_COMPOSITION", "home", "Audit 67 Right Wing"),
            event("MATERIAL_FLAGS_CHECKED", "render-flags-world"),
            event("MATERIAL_ROUTE_CHECKED", "world"),
        ]))


if __name__ == "__main__":
    unittest.main()
