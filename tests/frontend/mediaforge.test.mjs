import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { JSDOM } from 'jsdom';
const source = readFileSync(new URL('../../Jellyfin.Plugin.JellyfinEnhanced/js/enhanced/mediaforge.js', import.meta.url), 'utf8');
const settle = async () => { for (let i = 0; i < 10; i++) await new Promise(r => setImmediate(r)); };
function setup(format = 'movie') {
    const dom = new JSDOM('<body><div id="tray"></div></body>', { url: 'https://jellyfin.test/web/', runScripts: 'outside-only' });
    const w = dom.window, calls = [], resets = {};
    w.HTMLDialogElement.prototype.showModal = function () { this.open = true; };
    w.HTMLDialogElement.prototype.close = function () { this.open = false; this.dispatchEvent(new w.Event('close')); };
    const item = { id: 123, title: '<img src=x onerror=alert(1)>', format, year: 2026, status: 'none', season_choices: [{ season_number: 1, name: 'Season 1', episode_count: 5 }, { season_number: 2, name: 'Season 2', episode_count: 4, available: true }] };
    w.ApiClient = { getCurrentUserId: () => 'user-1', accessToken: () => 'jellyfin-secret', getUrl: p => 'https://jellyfin.test/' + p };
    w.JellyfinEnhanced = { helpers: { getHeaderButtonTray: () => w.document.getElementById('tray'), onBodyMutation: () => {} }, session: { onUserChange: (name, fn) => { resets[name] = fn; } } };
    w.fetch = async (url, options) => {
        calls.push({ url, options });
        const path = url.split('mediaforge/')[1];
        let data = path === 'config' ? { enabled: true } : path === 'login' ? { session: 'abc.def' } : path.startsWith('search?') ? [item] : path.startsWith('details/') ? item : path === 'defaults' ? { default_quality: '1080p', language_requirements: { versions: ['VF'] } } : path.startsWith('library?') ? { items: [], pagination: { total_pages: 1 } } : {};
        return { ok: true, status: 200, json: async () => data };
    };
    w.eval(source);
    const click = text => { const b = [...w.document.querySelectorAll('button')].find(b => b.textContent === text); assert.ok(b, text); b.click(); };
    async function login() {
        await settle(); click('MediaForge'); await settle();
        assert.equal(w.document.querySelector('input[type=password]'), null);
    }
    async function search() {
        const form = w.document.querySelector('dialog form'); form.querySelector('input').value = 'A & B';
        form.dispatchEvent(new w.Event('submit', { cancelable: true })); await settle();
        [...w.document.querySelectorAll('button')].find(b => b.textContent.includes(item.title)).click(); await settle();
    }
    return { w, dom, calls, resets, click, login, search };
}
test('movie request uses personal session, server defaults, and safe text rendering', async () => {
    const h = setup(); await h.login(); await h.search();
    assert.equal(h.w.document.querySelector('dialog img'), null);
    h.click('Request'); await settle();
    const call = h.calls.find(c => c.url.endsWith('/request'));
    assert.ok(call); const body = JSON.parse(call.options.body);
    assert.equal(body.tmdb_id, 123); assert.equal(body.quality, '1080p'); assert.equal(body.media_type, 'auto');
    assert.equal(call.options.headers['X-Emby-Token'], 'jellyfin-secret');
    assert.equal(call.options.headers['X-MediaForge-Session'], undefined);
    assert.ok(!h.calls.some(c => c.url.endsWith('/login')));
    assert.equal(h.w.localStorage.length, 0); assert.equal(h.w.sessionStorage.length, 0);
    h.dom.window.close();
});
test('series requires explicit season selection and does not request available seasons', async () => {
    const h = setup('tv'); await h.login(); await h.search(); h.click('Request'); await settle();
    assert.equal(h.calls.filter(c => c.url.endsWith('/request')).length, 0);
    const choices = h.w.document.querySelectorAll('input[type=checkbox]');
    assert.equal(choices[1].disabled, true); choices[0].checked = true;
    h.click('Request'); await settle();
    assert.deepEqual(JSON.parse(h.calls.find(c => c.url.endsWith('/request')).options.body).seasons, [1]);
    h.dom.window.close();
});
test('account changes close the panel and discard the MediaForge session', async () => {
    const h = setup(); await h.login(); h.resets.mediaforge(); await settle();
    assert.equal(h.w.document.querySelector('dialog'), null); h.click('MediaForge');
    assert.equal(h.w.document.querySelector('input[type=password]'), null);
    assert.ok(h.w.document.querySelector('input[type=search]'));
    h.dom.window.close();
});
