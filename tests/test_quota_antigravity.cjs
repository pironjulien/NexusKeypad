// Script de lecture passif de production, sans navigateur, clic, profil ou appel réseau.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../plugin/src/Trinite/QuotaAntigravity.cs'), 'utf8');
const extrait = source.match(/private const String Passif = """\r?\n([\s\S]*?)\r?\n\s*""";/);
assert.ok(extrait, 'Exécuter le JavaScript passif réellement livré.');
const script = new vm.Script(extrait[1]);
const rect = (left=10, top=10, width=300, height=120) => ({left,top,width,height,right:left+width,bottom:top+height});
class Element {
  constructor(attributes={}, text='', options={}) {
    this.attributes=attributes; this.hidden=!!options.hidden;
    this.style={display:'block',visibility:'visible',opacity:'1',contentVisibility:'visible',...options.style};
    this.rects=options.rects??[rect()]; this.children=[]; this.innerText=text;
    if(text) this.append({nodeValue:text});
  }
  append(child) { child.parentElement=this; this.children.push(child); return child; }
  getAttribute(name) { return this.attributes[name]??null; }
  hasAttribute(name) { return Object.hasOwn(this.attributes,name); }
  getClientRects() { return this.rects; }
  closest(selector) {
    const role=selector.match(/^\[role=(.*)\]$/)[1];
    for(let p=this;p;p=p.parentElement) if(p.getAttribute('role')===role)return p;
    return null;
  }
  querySelectorAll(selector) {
    assert.equal(selector,'button');
    return elements([this]).filter(x=>x.attributes.tag==='button');
  }
}
function elements(roots) {
  const all=[]; const visit=e=>{if(e instanceof Element){all.push(e);e.children.forEach(visit);}};
  roots.forEach(visit); return all;
}
const menu=(text, options={}, attrs={})=>new Element({role:'menu',...attrs},text,options);
const dialogue=(text, options={})=>{
  const d=new Element({role:'dialog'},text,options);d.append(new Element({tag:'button'},'Models'));return d;
};
function lire({expanded=true,trigger={},menus=[],dialogs=[]}={}) {
  const selector=new Element({'aria-expanded':String(expanded)},'',trigger);
  const all=elements([...menus,...dialogs]);
  return script.runInNewContext({
    window:{innerWidth:1200,innerHeight:800},NodeFilter:{SHOW_TEXT:4},getComputedStyle:e=>e.style,
    document:{
      querySelector:s=>{assert.equal(s,'[data-testid=model-selector-trigger]');return selector;},
      querySelectorAll:s=>{
        const role=s.match(/^\[role=(.*)\]$/)[1];return all.filter(e=>e.getAttribute('role')===role);
      },
      createTreeWalker:(root,kind)=>{
        assert.equal(kind,4);const texts=[];const visit=e=>e instanceof Element?e.children.forEach(visit):texts.push(e);
        visit(root);let i=0;return {nextNode:()=>texts[i++]??null};
      },
      createRange:()=>{let n;return {selectNodeContents:x=>n=x,getClientRects:()=>n.rects??n.parentElement.rects};},
    },
  },{timeout:1000});
}
const actuel='Usage\nGemini Models\nWeekly limit\n93% left\nResets in 6d 6h\n5h limit\n100% left';
const ancien='Gemini Models\nWeekly Limit Remaining\n93%\nFive Hour Limit Remaining\n100%';

test('Le panneau actuel et l’ancien panneau visible sont lus',()=>{
  assert.equal(lire({menus:[menu(actuel)]}),actuel);
  assert.equal(lire({menus:[menu(ancien)]}),ancien);
  assert.equal(lire({dialogs:[dialogue('Models & Usage\n'+ancien)]}),'Models & Usage\n'+ancien+'\nModels');
});
test('Sélecteur fermé ou invisible : aucun menu de quota passif',()=>{
  assert.equal(lire({expanded:false,menus:[menu(actuel)]}),null);
  assert.equal(lire({trigger:{style:{opacity:'0'}},menus:[menu(actuel)]}),null);
});
test('Menus fermés, cachés, transparents et hors écran ne sont jamais lus',()=>{
  for(const options of [{hidden:true},{style:{display:'none'}},{style:{visibility:'hidden'}},{style:{opacity:'0'}},
    {style:{contentVisibility:'hidden'}},{rects:[]},{rects:[rect(1500)]}])
    assert.equal(lire({menus:[menu(actuel,options)]}),null,JSON.stringify(options));
  for(const attrs of [{'data-closed':''},{'aria-hidden':'true'},{inert:''}])
    assert.equal(lire({menus:[menu(actuel,{},attrs)]}),null,JSON.stringify(attrs));
});
test('Le texte caché et les sous-menus ne contaminent pas le texte retourné',()=>{
  const parent=menu(actuel);
  parent.append(new Element({},'SECRET HIDDEN VALUE 1% left',{style:{opacity:'0'}}));
  parent.append(menu('SECRET CLOSED SUBMENU 1% left',{}, {'data-closed':''}));
  assert.equal(lire({menus:[parent]}),actuel);
  const autre=menu('View Usage');autre.append(menu(actuel,{}, {'data-closed':''}));
  assert.equal(lire({menus:[autre]}),null);
});
test('Deux panneaux visibles restent ambigus, sans fusion des groupes',()=>{
  assert.equal(lire({menus:[menu(actuel),menu(ancien)]}),null);
});
test('Le dialogue Models doit être réellement visible ; les autres dialogues sont ignorés',()=>{
  assert.equal(lire({dialogs:[dialogue('Models & Usage\n'+ancien,{hidden:true})]}),null);
  assert.equal(lire({dialogs:[new Element({role:'dialog'},'Models & Usage\n'+ancien)]}),null);
  const d=dialogue('Models & Usage\n'+ancien);d.children[1].style.visibility='hidden';
  assert.equal(lire({dialogs:[d]}),null);
});
