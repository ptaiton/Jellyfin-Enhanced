(function (JE) {
    'use strict';
    // Sessions live only in this module, never in localStorage or plugin settings.
    let session = '', dialog, enabled = false, checking = false, generation = 0;
    const fr = (navigator.language || '').startsWith('fr');
    const t = (en, french) => fr ? french : en;
    const labels = {
        none: t('Not requested', 'Non demandé'),
        requested: t('Requested', 'Demandé'), downloading: t('Downloading', 'Téléchargement'),
        available: t('Available', 'Disponible'), pending: t('Pending', 'En attente'),
        error: t('Error', 'Erreur'), failed: t('Failed', 'Échec'), missing: t('Missing', 'Manquant')
    };
    function element(tag, text, parent) {
        const node = document.createElement(tag);
        if (text != null) node.textContent = text;
        parent?.append(node);
        return node;
    }
    function button(text, parent, action) {
        const node = element('button', text, parent);
        node.type = 'button';
        node.className = 'emby-button raised';
        node.addEventListener('click', action);
        return node;
    }
    async function api(path, body) {
        const epoch = generation;
        const response = await fetch(ApiClient.getUrl('JellyfinEnhanced/mediaforge/' + path), {
            method: body === undefined ? 'GET' : 'POST',
            headers: {
                'X-Emby-Token': ApiClient.accessToken(), 'X-MediaForge-Session': session,
                'Content-Type': 'application/json'
            },
            body: body === undefined ? undefined : JSON.stringify(body), cache: 'no-store'
        });
        const data = await response.json().catch(() => ({}));
        if (epoch !== generation) throw new Error(t('Account changed.', 'Le compte a changé.'));
        if (!response.ok) {
            if (response.status === 401) session = '';
            throw new Error(data.error || t('MediaForge request failed.', 'Échec de la requête MediaForge.'));
        }
        return data;
    }
    function field(parent, name, type = 'text') {
        const label = element('label', name, parent);
        label.style.display = 'block';
        const input = element('input', null, label);
        input.className = 'emby-input'; input.type = type; input.required = true;
        input.style.cssText = 'display:block;box-sizing:border-box;width:100%;margin:8px 0 16px';
        return input;
    }
    function open() {
        dialog?.remove();
        const previousFocus = document.activeElement;
        dialog = element('dialog', null, document.body);
        const current = dialog;
        current.style.cssText = 'color:inherit;background:#202124;border:1px solid #666;border-radius:12px;padding:24px;width:680px;max-width:85vw;max-height:80vh;overflow:auto';
        element('h2', 'MediaForge', current);
        button(t('Close', 'Fermer'), current, () => current.close());
        current.addEventListener('close', () => { current.remove(); previousFocus?.focus(); });
        const content = element('div', null, current);
        const message = element('p', '', current);
        message.setAttribute('role', 'status');
        async function run(action, control) {
            if (control) control.disabled = true;
            message.textContent = '';
            try { await action(); }
            catch (error) {
                message.textContent = error.message;
                if (!session) login();
            }
            finally { if (control) control.disabled = false; }
        }
        function login() {
            content.replaceChildren();
            element('p', t('Sign in with your MediaForge account. Your password is not saved.', 'Connectez-vous avec votre compte MediaForge. Votre mot de passe ne sera pas enregistré.'), content);
            const form = element('form', null, content);
            const username = field(form, t('Username', 'Utilisateur'));
            username.autocomplete = 'username';
            const password = field(form, t('Password', 'Mot de passe'), 'password');
            password.autocomplete = 'current-password';
            const submit = element('button', t('Sign in', 'Connexion'), form);
            submit.type = 'submit'; submit.className = 'emby-button raised';
            form.addEventListener('submit', event => {
                event.preventDefault();
                const credentials = { username: username.value, password: password.value };
                password.value = '';
                void run(async () => {
                    const result = await api('login', credentials);
                    session = result.session;
                    search();
                }, submit);
            });
        }
        async function showDetails(item, target) {
            const data = await api(`details/${item.format}/${item.id}`);
            const defaults = await api('defaults');
            target.replaceChildren();
            element('h3', data.title, target);
            element('p', data.overview, target);
            const choices = [];
            if (data.format === 'tv') {
                for (const season of data.season_choices || []) {
                    const label = element('label', '', target); label.style.display = 'block';
                    const input = element('input', null, label); input.type = 'checkbox';
                    input.disabled = season.available || season.requested;
                    label.append(document.createTextNode(` ${season.name} (${season.episode_count})${season.available ? ' — ' + labels.available : season.requested ? ' — ' + labels.requested : ''}`));
                    choices.push({ input, number: season.season_number });
                }
            }
            const request = button(t('Request', 'Demander'), target, () => void run(async () => {
                const seasons = choices.filter(c => c.input.checked).map(c => c.number);
                if (data.format === 'tv' && !seasons.length) throw new Error(t('Select at least one season.', 'Sélectionnez au moins une saison.'));
                await api('request', {
                    tmdb_id: data.id, title: data.title, media_type: 'auto', format: data.format,
                    quality: defaults.default_quality, language: 'MULTI', language_requirements: defaults.language_requirements,
                    retention: 'indefinite', poster: data.poster, year: data.year, rating: data.rating,
                    genre: data.genre, original_language: data.original_language, origin_countries: data.origin_countries,
                    overview: data.overview, original_title: data.original_title,
                    ...(data.format === 'tv' ? { seasons } : {})
                });
                await showDetails(item, target);
                message.textContent = t('Request sent to MediaForge.', 'Demande envoyée à MediaForge.');
            }, request));
            if (data.format === 'movie' && data.status && !['none', 'missing'].includes(data.status)) {
                request.disabled = true;
                request.textContent = labels[data.status] || data.status;
            }
        }
        function search() {
            content.replaceChildren();
            button(t('Sign out', 'Déconnexion'), content, () => void run(async () => {
                try { await api('logout', {}); } finally { session = ''; login(); }
            }));
            const form = element('form', null, content);
            const query = field(form, t('Find a film or series', 'Rechercher un film ou une série'), 'search');
            const submit = element('button', t('Search', 'Rechercher'), form); submit.type = 'submit';
            const results = element('div', null, content);
            const details = element('div', null, content);
            let selection = 0;
            form.addEventListener('submit', event => {
                event.preventDefault();
                void run(async () => {
                    const items = await api('search?query=' + encodeURIComponent(query.value));
                    results.replaceChildren(); details.replaceChildren(); selection++;
                    if (!items.length) element('p', t('No results.', 'Aucun résultat.'), results);
                    for (const item of items) {
                        button(`${item.title} (${item.year || '—'}) — ${item.format === 'tv' ? t('Series', 'Série') : t('Film', 'Film')} · ${labels[item.status] || item.status || ''}`, results, () => {
                            const ticket = ++selection;
                            // A separate node prevents a slower previous selection overwriting the latest one.
                            const target = element('div'); details.replaceChildren(target);
                            void run(async () => { await showDetails(item, target); if (ticket !== selection) target.remove(); });
                        });
                    }
                }, submit);
            });
            let page = 1;
            const tracked = element('div', null, content);
            async function library() {
                const data = await api('library?page=' + page);
                tracked.replaceChildren();
                element('h3', t('Tracked requests', 'Demandes suivies'), tracked);
                for (const item of data.items || []) element('p', `${item.title} — ${labels[item.status] || item.status}`, tracked);
                const prev = button(t('Previous', 'Précédent'), tracked, () => { page--; void run(library); });
                prev.disabled = page <= 1;
                const next = button(t('Next', 'Suivant'), tracked, () => { page++; void run(library); });
                next.disabled = page >= data.pagination.total_pages;
                button(t('Refresh', 'Actualiser'), tracked, () => void run(library));
            }
            void run(library);
            query.focus();
        }
        current.showModal();
        if (session) search(); else login();
    }
    function mount() {
        if (!enabled || !ApiClient.getCurrentUserId() || document.getElementById('je-mediaforge')) return;
        const tray = JE.helpers?.getHeaderButtonTray?.();
        if (!tray) return;
        const action = button('MediaForge', tray, open);
        action.id = 'je-mediaforge'; action.className = 'headerButton emby-button';
        action.title = 'MediaForge'; action.setAttribute('aria-label', 'MediaForge');
    }
    async function check() {
        if (checking || !window.ApiClient?.getCurrentUserId()) return;
        checking = true;
        try { enabled = (await api('config')).enabled; mount(); }
        catch (_) { enabled = false; }
        finally { checking = false; }
    }
    JE.session.onUserChange('mediaforge', () => {
        generation++; session = ''; enabled = false;
        dialog?.close(); document.getElementById('je-mediaforge')?.remove();
        void check();
    });
    document.addEventListener('viewshow', () => { void check(); });
    JE.helpers.onBodyMutation('mediaforge', mount);
    void check();
})(window.JellyfinEnhanced);
