import copy
import io
import json
from pathlib import Path
import unittest
from urllib.error import HTTPError, URLError

import announce


CONFIG = json.loads(Path(__file__).with_name('config.json').read_text(encoding='utf-8'))
CONFIG['channel_id'] = '789'  # Fake channel used by the network doubles below.
CONFIG['forum_channel'] = False
RELEASE = {
    'id': 123, 'draft': False, 'prerelease': True,
    'name': 'Campaign Studio update', 'tag_name': 'beta-example',
    'body': 'Fixed player exports.\n\n@everyone <@123> are literal patch-note text.',
    'published_at': '2026-10-15T12:00:00Z',
    'html_url': f"https://github.com/{CONFIG['repository']}/releases/tag/beta-example",
    'assets': [{'name': CONFIG['installer_name'], 'state': 'uploaded',
                'browser_download_url': f"https://github.com/{CONFIG['repository']}/releases/download/beta-example/setup.exe"}],
}
WEBHOOK = 'https://discord.com/api/webhooks/123/example-not-a-real-token'


class DeliveryChecks(unittest.TestCase):
    def test_beta_release_has_notes_installer_and_no_mentions(self):
        payload = announce.make_payload(RELEASE, CONFIG)
        self.assertEqual(payload['allowed_mentions'], {'parse': []})
        embed = payload['embeds'][0]
        self.assertIn(RELEASE['body'], embed['description'])
        self.assertIn('Beta', embed['fields'][1]['value'])
        self.assertIn('/setup.exe', embed['fields'][2]['value'])

    def test_stable_and_empty_notes(self):
        release = dict(RELEASE, prerelease=False, body=None)
        embed = announce.make_payload(release, CONFIG)['embeds'][0]
        self.assertEqual(embed['fields'][1]['value'], 'Stable')
        self.assertIn('release page', embed['description'])

    def test_long_unicode_stays_within_discord_limits(self):
        release = dict(RELEASE, body='🏒' * 10000, name='🏒' * 500, tag_name='x' * 500)
        embed = announce.make_payload(release, CONFIG)['embeds'][0]
        units = lambda s: len(s.encode('utf-16-le')) // 2
        self.assertLessEqual(units(embed['title']), 256)
        self.assertLessEqual(units(embed['description']), 4096)
        total = units(embed['title']) + units(embed['description']) + units(embed['footer']['text'])
        for field in embed['fields']:
            self.assertLessEqual(units(field['value']), 1024)
            total += units(field['name']) + units(field['value'])
        self.assertLessEqual(total, 6000)
        self.assertIn('Full patch notes', embed['description'])

    def test_drafts_and_missing_installer_rejected(self):
        for release in [dict(RELEASE, draft=True), dict(RELEASE, published_at=None), dict(RELEASE, assets=[])]:
            with self.subTest(release=release), self.assertRaises(announce.AnnouncementError):
                announce.make_payload(release, CONFIG)

    def test_non_github_download_rejected(self):
        release = copy.deepcopy(RELEASE)
        release['assets'][0]['browser_download_url'] = 'https://example.com/setup.exe'
        with self.assertRaises(announce.AnnouncementError):
            announce.make_payload(release, CONFIG)

    def test_webhook_validation_never_echoes_secret(self):
        for url in ['http://discord.com/api/webhooks/123/secret',
                    'https://discord.com.evil.example/api/webhooks/123/secret',
                    WEBHOOK + '?thread_id=123']:
            with self.subTest(url=url), self.assertRaises(announce.AnnouncementError) as error:
                announce.validate_webhook(url)
            self.assertNotIn('secret', str(error.exception))

    def test_wrong_server_or_channel_never_posts(self):
        for config, guild in [(CONFIG, 'wrong'), (dict(CONFIG, channel_id='wanted'), CONFIG['guild_id'])]:
            calls = []
            def request(url, **kwargs):
                calls.append(kwargs.get('method', 'GET'))
                return {'guild_id': guild, 'channel_id': 'actual', 'type': 1}
            with self.assertRaises(announce.AnnouncementError):
                announce.deliver({}, WEBHOOK, config, request=request)
            self.assertEqual(calls, ['GET'])

    def test_send_requires_confirmation_and_returns_message_link(self):
        calls = []
        def request(url, **kwargs):
            calls.append((url, kwargs))
            return ({'id': '456', 'channel_id': '789'} if kwargs.get('method') == 'POST'
                    else {'guild_id': CONFIG['guild_id'], 'channel_id': '789', 'type': 1})
        result = announce.deliver({'allowed_mentions': {'parse': []}}, WEBHOOK, CONFIG, request=request)
        self.assertEqual(result, f"https://discord.com/channels/{CONFIG['guild_id']}/789/456")
        self.assertTrue(calls[-1][0].endswith('?wait=true'))

    def test_unconfirmed_post_is_failure(self):
        def request(url, **kwargs):
            return {} if kwargs.get('method') == 'POST' else {'guild_id': CONFIG['guild_id'], 'channel_id': '789', 'type': 1}
        with self.assertRaises(announce.AnnouncementError):
            announce.deliver({}, WEBHOOK, CONFIG, request=request)

    def test_forum_post_has_bounded_title_and_returns_new_thread_link(self):
        config = dict(CONFIG, forum_channel=True)
        payload = announce.make_payload(dict(RELEASE, name='🏒' * 100), config)
        self.assertLessEqual(len(payload['thread_name'].encode('utf-16-le')) // 2, 100)
        def request(url, **kwargs):
            if kwargs.get('method') == 'POST':
                self.assertEqual(kwargs['data'], payload)
                return {'id': '456', 'channel_id': '999'}
            return {'guild_id': config['guild_id'], 'channel_id': config['channel_id'], 'type': 1}
        self.assertEqual(announce.deliver(payload, WEBHOOK, config, request=request),
                         f"https://discord.com/channels/{config['guild_id']}/999/456")

    def test_forum_without_title_never_posts(self):
        calls = []
        def request(url, **kwargs):
            calls.append(kwargs.get('method', 'GET'))
            return {'guild_id': CONFIG['guild_id'], 'channel_id': '789', 'type': 1}
        with self.assertRaises(announce.AnnouncementError):
            announce.deliver({}, WEBHOOK, dict(CONFIG, forum_channel=True), request=request)
        self.assertEqual(calls, ['GET'])

    def test_regular_channel_response_cannot_switch_to_another_channel(self):
        def request(url, **kwargs):
            if kwargs.get('method') == 'POST': return {'id': '456', 'channel_id': '999'}
            return {'guild_id': CONFIG['guild_id'], 'channel_id': '789', 'type': 1}
        with self.assertRaises(announce.AnnouncementError):
            announce.deliver({}, WEBHOOK, CONFIG, request=request)

    def test_release_refresh_waits_for_installer_on_exact_id(self):
        calls, sleeps = [], []
        def request(url, **kwargs):
            calls.append(url)
            return RELEASE if len(calls) == 2 else dict(RELEASE, assets=[])
        event = {'repository': {'full_name': CONFIG['repository']}, 'release': RELEASE, 'action': 'published'}
        self.assertEqual(announce.load_release(event, 'release', '', CONFIG, '', True, request, sleeps.append), RELEASE)
        self.assertEqual(sleeps, [15])
        self.assertTrue(all(url.endswith('/releases/123') for url in calls))

    def test_fork_and_edited_events_rejected(self):
        for event in [ {'repository': {'full_name': 'someone/fork'}},
                      {'repository': {'full_name': CONFIG['repository']}, 'release': RELEASE, 'action': 'edited'} ]:
            with self.assertRaises(announce.AnnouncementError):
                announce.load_release(event, 'release', '', CONFIG, '', True)

    def test_timeout_not_retried_or_secret_logged(self):
        class Opener:
            calls = 0
            def open(self, request, timeout):
                self.calls += 1
                raise URLError(WEBHOOK)
        opener = Opener()
        with self.assertRaises(announce.AnnouncementError) as error:
            announce.request_json(WEBHOOK, method='POST', data={}, opener=opener)
        self.assertEqual(opener.calls, 1)
        self.assertNotIn(WEBHOOK, str(error.exception))

    def test_rate_limit_retried_after_requested_delay(self):
        class Opener:
            calls = 0
            def open(self, request, timeout):
                self.calls += 1
                if self.calls == 1:
                    raise HTTPError(WEBHOOK, 429, 'limited', {}, io.BytesIO(b'{"retry_after":2}'))
                return io.BytesIO(b'{"id":"123"}')
        sleeps = []
        self.assertEqual(announce.request_json(WEBHOOK, method='POST', data={}, opener=Opener(), sleep=sleeps.append), {'id': '123'})
        self.assertEqual(sleeps, [2])


if __name__ == '__main__':
    unittest.main()
