import test from 'node:test';
import assert from 'node:assert/strict';
import {createHarness} from './helpers/harness.mjs';
import {readFileSync} from 'node:fs';

test('MediaForge uses the native Seerr API and localizes provider names without creating a panel',async t=>{
    const calls=[];
    const h=createHarness({JE:{pluginConfig:{MediaForgeEnabled:true},t:key=>'Search Seerr '+key,
        core:{api:{fetch:async(url)=>{calls.push(url);return {results:[{id:42,mediaType:'movie',title:'Dune',mediaInfo:null}],totalPages:1};},plugin:async(path,options)=>{calls.push([path,options.body]);return {id:7};}}},
        helpers:{trackUsage:()=>{}},requestManager:{clearCacheMatching:()=>{}},session:{onUserChange:()=>{},getEpoch:()=>0,isCurrent:()=>true}
    }});t.after(()=>h.close());
    h.load('enhanced/mediaforge-provider.js');h.load('jellyseerr/api.js');
    assert.equal(h.JE.t('results'),'Search MediaForge results');
    await h.JE.jellyseerrAPI.search('Dune',1);
    await h.JE.jellyseerrAPI.requestTvSeasons(42,[1,3]);
    assert.ok(calls.some(c=>typeof c==='string'&&c.includes('/JellyfinEnhanced/jellyseerr/search')));
    assert.deepEqual(JSON.parse(JSON.stringify(calls.find(c=>Array.isArray(c))[1])),{mediaType:'tv',mediaId:42,seasons:[1,3]});
    assert.equal(h.document.querySelector('dialog'),null);
    assert.equal(h.document.querySelector('#je-mediaforge'),null);
    const manifest=JSON.parse(readFileSync(new URL('../../Jellyfin.Plugin.JellyfinEnhanced/js/component-scripts.json',import.meta.url),'utf8'));
    assert.ok(!manifest.includes('enhanced/mediaforge.js'));
});
