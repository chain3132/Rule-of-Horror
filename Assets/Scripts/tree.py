import re,sys,collections
p=r"C:/Users/chain/Documents/GitHub/Rule of Horror/Rule of Horror/Assets/Scenes/Rule4.unity"
txt=open(p,encoding='utf-8',errors='replace').read()
blocks=re.split(r'\n--- ',txt)
go={}; rt={}; comp={}
for b in blocks:
    m=re.match(r'!u!(\d+) &(-?\d+)',b)
    if not m: continue
    cls,fid=m.group(1),m.group(2)
    if cls=='1':
        name=re.search(r'm_Name: (.*)',b)
        comps=re.findall(r'component: \{fileID: (-?\d+)\}',b)
        act=re.search(r'm_IsActive: (\d)',b)
        go[fid]={'name':name.group(1).strip() if name else '?','comps':comps,'active':act.group(1) if act else '?'}
    elif cls in ('224','4'):
        g=re.search(r'm_GameObject: \{fileID: (-?\d+)\}',b)
        ch=re.findall(r'- \{fileID: (-?\d+)\}',b.split('m_Children:')[1].split('m_Father:')[0]) if 'm_Children:' in b else []
        fa=re.search(r'm_Father: \{fileID: (-?\d+)\}',b)
        amin=re.search(r'm_AnchorMin: \{x: ([-\d.]+), y: ([-\d.]+)\}',b)
        amax=re.search(r'm_AnchorMax: \{x: ([-\d.]+), y: ([-\d.]+)\}',b)
        piv=re.search(r'm_AnchoredPosition: \{x: ([-\d.]+), y: ([-\d.]+)\}',b)
        rt[fid]={'go':g.group(1) if g else None,'children':ch,'father':fa.group(1) if fa else '0',
                 'amin':amin.groups() if amin else None,'amax':amax.groups() if amax else None,'pos':piv.groups() if piv else None,'cls':cls}
    else:
        g=re.search(r'm_GameObject: \{fileID: (-?\d+)\}',b)
        if g:
            t=re.search(r'm_EditorClassIdentifier: (.*)',b)
            comp.setdefault(g.group(1),[]).append((t.group(1).strip().split('::')[-1] if t else 'cls'+cls))
def show(trans,d=0):
    r=rt.get(trans)
    if not r: return
    g=go.get(r['go'],{})
    info=f"{'  '*d}{g.get('name','?')} [act={g.get('active')}]"
    if r['amin']: info+=f" anchor {r['amin']}-{r['amax']} pos {r['pos']}"
    cs=comp.get(r['go'],[])
    if cs: info+="  {"+", ".join(cs)+"}"
    print(info)
    for c in r['children']: show(c,d+1)
for target in ['KeybindCanvas','KeypromptUI','KeyCap']:
    for fid,g in go.items():
        if g['name']==target:
            # find its transform
            for c in g['comps']:
                if c in rt:
                    print("="*20,target,"root transform",c, "father",rt[c]['father'])
                    show(c)
