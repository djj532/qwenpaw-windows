import{getLanguage as c}from"./getLanguage-DPW4aTiG.js";const s=n=>n.replace(/\n+$/,"");function g(n){return c(n)}function a(n){return n.includes("::")?n.slice(n.indexOf("::")+2):n}function d(n,u){const t=u.getValueInRange(n),i=n.startLineNumber;let e=n.endLineNumber;e>i&&n.endColumn===1&&(e-=1);const o=[];for(let r=i;r<=e;r+=1)o.push(u.getLineContent(r));return s(t)===o.join(`
`)?{mode:"lines-only",code:t,startLine:i,endLine:e}:{mode:"with-code",code:t,startLine:i,endLine:e}}function $(n,u,t,i,e){const o=a(n);if(e==="whole-file")return o;const r=t===i?`${t}`:`${t}-${i}`;if(e==="lines-only")return`${o}:${r}`;const l=g(n);return`${o}:${r}
\`\`\`${l}
${u}
\`\`\``}export{d as detectCopyMode,$ as formatSelectionForChat,g as getEditorLanguage,a as visibleEditorPath};
