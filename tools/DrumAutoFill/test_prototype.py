import unittest

from prototype import events_from_peaks, match_moments, score_events


class PreviewTests(unittest.TestCase):
    def test_nearest_within_inclusive_tolerance_and_stable_stacked_object(self):
        moments = [dict(timeMs=100, objectIndex=i, node=0, volume=55) for i in (2, 1)]
        events = [dict(timeMs=130, score=.8, drum="kick"),
                  dict(timeMs=130.01, score=.9, drum="snare")]
        match_moments(events, moments, 30)
        self.assertEqual(events[0]["moment"]["objectIndex"], 1)
        self.assertEqual(events[0]["moment"]["volume"], 55)
        self.assertEqual(events[0]["snapDeltaMs"], -30)
        self.assertEqual(events[1]["status"], "unmatched")

    def test_same_drum_collision_keeps_stronger_but_different_drums_can_coexist(self):
        moments = [dict(timeMs=100, objectIndex=0, node=0, volume=70)]
        events = [dict(timeMs=99, score=.6, drum="kick"),
                  dict(timeMs=102, score=.9, drum="kick"),
                  dict(timeMs=102, score=.7, drum="snare")]
        match_moments(events, moments, 30)
        self.assertEqual([e["status"] for e in events], ["duplicate", "matched", "matched"])

    def test_no_objects_do_not_invent_matches(self):
        event = dict(timeMs=-20, score=.8, drum="kick")
        match_moments([event], [], 30)
        self.assertEqual(event["status"], "unmatched")

    def test_scoring_is_one_to_one_and_reports_false_snares_with_no_reference(self):
        events = [dict(timeMs=t, drum=d) for t, d in
                  [(90, "kick"), (110, "kick"), (200, "snare")]]
        scores = score_events(events, [(100, "kick")])
        self.assertEqual(scores["kick"]["correct"], 1)
        self.assertEqual(scores["kick"]["precision"], .5)
        self.assertEqual(scores["snare"]["precision"], 0)
        self.assertIsNone(scores["snare"]["recall"])

    def test_tom_peaks_are_excluded_while_offsets_and_simultaneous_kick_snare_survive(self):
        peaks = {35: [0, .02], 38: [0], 47: [0, .01]}
        activation = [[[.8, .7, 1.0], [.1, .1, 1.0], [.9, .1, 1.0]]]
        events = events_from_peaks(peaks, activation, [35, 38, 47], 20, 30017)
        self.assertEqual([(e["drum"], e["timeMs"]) for e in events],
                         [("kick", 30017), ("snare", 30017)])
        self.assertEqual([e["score"] for e in events], [.8, .7])
        scores = score_events(events, [(30017, "kick"), (30017, "snare"), (30017, "tom")])
        self.assertEqual(set(scores), {"kick", "snare"})


if __name__ == "__main__":
    unittest.main()
