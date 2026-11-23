import assert from 'node:assert/strict';
import { signOutSession } from '../../ASTRA-Frontend-latest/src/pages/auth-logout.ts';

function scenario(responses) {
  const calls = [];
  return { calls, request: async (path, options) => {
    calls.push({ path, options });
    assert.ok(responses.length, 'No unexpected extra logout or session requests');
    return responses.shift();
  }};
}
const reply = (status, body) => new Response(body ? JSON.stringify(body) : null, {status});
for (const status of [200, 401]) {
  const s = scenario([reply(status)]);
  await signOutSession('old', s.request);
  assert.equal(s.calls.length, 1);
}
const refreshed = scenario([reply(403), reply(200, {csrfToken: 'fresh'}), reply(200)]);
await signOutSession('old', refreshed.request);
assert.equal(refreshed.calls[2].options.headers['x-csrf-token'], 'fresh');
const expired = scenario([reply(403), reply(401)]);
await signOutSession('old', expired.request);
const denied = scenario([reply(403), reply(200, {csrfToken: 'old'})]);
await assert.rejects(signOutSession('old', denied.request), /still signed in/);
assert.equal(denied.calls.length, 2);
const retryDenied = scenario([reply(403), reply(200, {csrfToken: 'fresh'}), reply(403)]);
await assert.rejects(signOutSession('old', retryDenied.request), /still signed in/);
assert.equal(retryDenied.calls.length, 3);
const unavailable = scenario([reply(503)]);
await assert.rejects(signOutSession('old', unavailable.request), /could not be completed/);
await assert.rejects(signOutSession('old', async () => {throw new Error('network');}), /network/);
console.log('PASS: successful, expired, stale-token, blocked, server-error and network-failure logout cases.');
