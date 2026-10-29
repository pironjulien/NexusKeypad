// Contrat DOM du script réellement livré : aucune application, aucun CDP, aucun clic.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../plugin/src/Trinite/Js.cs'), 'utf8');
const extrait = source.match(/public const String UtilisationAntigravityOuverte = """\r?\n([\s\S]*?)\r?\n\s*""";/);
assert.ok(extrait, 'Le test exécute la constante JavaScript de production.');
const script = new vm.Script(extrait[1]);

const rectangle = (left = 10, top = 10, width = 300, height = 120) =>
  ({ left, top, width, height, right: left + width, bottom: top + height });

class Element {
  constructor(attributes = {}, text = '', options = {}) {
    this.attributes = attributes;
    this.hidden = !!options.hidden;
    this.style = { display: 'block', visibility: 'visible', opacity: '1', contentVisibility: 'visible', ...options.style };
    this.rects = options.rects ?? [rectangle()];
    this.children = [];
    if (text) this.append({ nodeValue: text });
  }
  append(child) { child.parentElement = this; this.children.push(child); return child; }
  getAttribute(name) { return this.attributes[name] ?? null; }
  hasAttribute(name) { return Object.hasOwn(this.attributes, name); }
  getClientRects() { return this.rects; }
  closest(selector) {
    assert.equal(selector, '[role=menu]');
    for (let p = this; p; p = p.parentElement) if (p.getAttribute('role') === 'menu') return p;
    return null;
  }
}

function fixture({ absent = false, expanded = true, trigger = {}, menus = [] } = {}) {
  const selector = absent ? null : new Element({ 'data-testid': 'model-selector-trigger', 'aria-expanded': String(expanded) }, '', trigger);
  const elements = [];
  const visit = e => { if (e instanceof Element) { elements.push(e); e.children.forEach(visit); } };
  menus.forEach(visit);
  return {
    window: { innerWidth: 1200, innerHeight: 800 },
    NodeFilter: { SHOW_TEXT: 4 },
    getComputedStyle: e => e.style,
    document: {
      querySelector: selectorText => {
        assert.equal(selectorText, '[data-testid=model-selector-trigger]');
        return selector;
      },
      querySelectorAll: selectorText => {
        assert.equal(selectorText, '[role=menu]');
        return elements.filter(e => e.getAttribute('role') === 'menu');
      },
      createTreeWalker: (root, kind) => {
        assert.equal(kind, 4);
        const texts = [];
        const collect = e => e instanceof Element ? e.children.forEach(collect) : texts.push(e);
        collect(root);
        let i = 0;
        return { nextNode: () => texts[i++] ?? null };
      },
      createRange: () => {
        let text;
        return { selectNodeContents: n => { text = n; }, getClientRects: () => text.rects ?? text.parentElement.rects };
      },
    },
  };
}

const menu = (text, options = {}, attributes = {}) => new Element({ role: 'menu', ...attributes }, text, options);
const lire = options => script.runInNewContext(fixture(options), { timeout: 1000 });
const actuel = 'Usage Gemini Models Weekly limit 93% left 5h limit 93% left';

test('Ancien et nouveau panneau visible sont reconnus, sans nombre de menus imposé', () => {
  assert.equal(lire({ menus: [menu('Limit Remaining 76%')] }), true);
  assert.equal(lire({ menus: [menu(actuel)] }), true);
  assert.equal(lire({ menus: [menu('Usage Gemini Models 5h limit 0% left')] }), true);
  const separe = menu('Usage');
  separe.append(new Element({}, 'Weekly'));
  separe.append(new Element({}, 'limit'));
  separe.append(new Element({}, '93'));
  separe.append(new Element({}, '% left'));
  assert.equal(lire({ menus: [separe] }), true);
});

test('Absence et sélecteur fermé restent inconnue ou fermé, même avec un ancien menu présent', () => {
  assert.equal(lire({ absent: true, menus: [menu(actuel)] }), null);
  assert.equal(lire({ expanded: false, menus: [menu(actuel)] }), false);
  assert.equal(lire({ trigger: { hidden: true }, menus: [menu(actuel)] }), false);
});

test('Menu caché, transparent, fermé ou hors viewport ne prouve jamais une ouverture', () => {
  for (const options of [{ hidden: true }, { style: { display: 'none' } }, { style: { visibility: 'hidden' } },
    { style: { opacity: '0' } }, { style: { contentVisibility: 'hidden' } }, { rects: [] },
    { rects: [rectangle(1300)] }, { rects: [rectangle(10, -200)] }, { rects: [rectangle(10, 10, 0)] }]) {
    assert.equal(lire({ menus: [menu(actuel, options)] }), false, JSON.stringify(options));
  }
  for (const attributes of [{ 'data-closed': '' }, { 'aria-hidden': 'true' }, { inert: '' }]) {
    assert.equal(lire({ menus: [menu(actuel, {}, attributes)] }), false, JSON.stringify(attributes));
  }
  const parent = new Element({}, '', { style: { opacity: '0' } });
  const enfant = parent.append(menu(actuel));
  assert.equal(lire({ menus: [enfant] }), false, 'Un ancêtre transparent masque le panneau.');
});

test('Les fragments cachés et sous-menus fermés ne contaminent pas un menu parent visible', () => {
  const parent = menu('View Usage Gemini Models');
  parent.append(menu(actuel, {}, { 'data-closed': '' }));
  parent.append(new Element({}, 'Weekly limit 93% left', { style: { opacity: '0' } }));
  assert.equal(lire({ menus: [parent] }), false);
  const horsVue = menu('Usage');
  horsVue.append(new Element({}, 'Weekly limit 93% left', { rects: [rectangle(1300)] }));
  assert.equal(lire({ menus: [horsVue] }), false);
});

test('Un menu modèle ou du texte incomplet ne devient pas une preuve de quotas', () => {
  for (const text of ['Gemini Models View Usage 93%', 'Usage Weekly limit', 'Usage 93% left', 'Weekly limit 93% left', 'Limit Remaining %']) {
    assert.equal(lire({ menus: [menu(text)] }), false, text);
  }
});
