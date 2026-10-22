(function (JE) {
    'use strict';
    // Keep Enhanced's native search, details, request and download components.
    // Their existing translation keys remain compatible across both providers.
    const translate = JE.t;
    JE.t = function (...args) {
        const result = translate.apply(this, args);
        return JE.pluginConfig?.MediaForgeEnabled && typeof result === 'string'
            ? result.replace(/Jellyseerr|Seerr/gi, 'MediaForge') : result;
    };
})(window.JellyfinEnhanced);
