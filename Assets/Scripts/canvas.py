import re
p=r"C:/Users/chain/Documents/GitHub/Rule of Horror/Rule of Horror/Assets/Scenes/Rule4.unity"
txt=open(p,encoding='utf-8',errors='replace').read()
blocks=re.split(r'\n--- ',txt)
go={}; byfid={}
for b in blocks:
    m=re.match(r'!u!(\d+) &(-?\d+)',b)
    if not m: continue
    cls,fid=m.group(1),m.group(2)
    byfid[fid]=(cls,b)
    if cls=='1':
        n=re.search(r'm_Name: (.*)',b)
        go[fid]=n.group(1).strip() if n else '?'
for fid,(cls,b) in byfid.items():
    if 'CanvasScaler' in b and cls=='114':
        g=re.search(r'm_GameObject: \{fileID: (-?\d+)\}',b)
        name=go.get(g.group(1),'?') if g else '?'
        ref=re.search(r'm_ReferenceResolution: \{x: ([-\d.]+), y: ([-\d.]+)\}',b)
        mode=re.search(r'm_UiScaleMode: (\d+)',b)
        match=re.search(r'm_ScreenMatchMode: (\d+)',b)
        mw=re.search(r'm_MatchWidthOrHeight: ([-\d.]+)',b)
        print(f"CanvasScaler on {name}: mode={mode.group(1) if mode else '?'} ref={ref.groups() if ref else None} matchmode={match.group(1) if match else '?'} match={mw.group(1) if mw else '?'}")
    if cls=='223':
        g=re.search(r'm_GameObject: \{fileID: (-?\d+)\}',b)
        name=go.get(g.group(1),'?') if g else '?'
        rm=re.search(r'm_RenderMode: (\d+)',b)
        cam=re.search(r'm_Camera: \{fileID: (-?\d+)\}',b)
        print(f"  Canvas on {name}: renderMode={rm.group(1) if rm else '?'} camera={cam.group(1) if cam else '-'}")
