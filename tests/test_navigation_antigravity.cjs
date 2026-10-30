// Postconditions DOM du script réellement livré : aucun navigateur et aucun geste.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../plugin/src/Trinite/Js.cs'), 'utf8');
const extrait = source.match(/public const String EtatNavigationAntigravity = """\r?\n([\s\S]*?)\r?\n\s*""";/);
assert.ok(extrait, 'Exécuter la postcondition de navigation de production.');
const script = new vm.Script(`(() => {${extrait[1]}})()`);
function lire({ route='/c/target', ready='complete', view=false, model=false } = {}) {
  return script.runInNewContext({id:'target',location:{pathname:route},document:{readyState:ready,
    querySelector:s=>s==='[data-testid=conversation-view]' ? view : s==='[data-testid=model-selector-trigger]' ? model : null}});
}
test('La conversation affichée reste reconnue sans éditeur ni sélecteur de modèle', () => assert.equal(lire({view:true}), 'open'));
test('Une URL seule ne prouve pas une conversation chargée', () => assert.equal(lire(), null));
test('Un éditeur à la bonne URL sans vue de conversation ne prouve pas son ouverture', () => assert.equal(lire({model:true}), 'ready'));
test('Le chargement du document reste inconnu même avec un ancien nœud', () => assert.equal(lire({view:true,ready:'loading'}), null));
test('La page d’accueil et une autre conversation peuvent recevoir le geste unique', () => {
  assert.equal(lire({route:'/',model:true}), 'ready');
  assert.equal(lire({route:'/c/other',view:true}), 'ready');
});
