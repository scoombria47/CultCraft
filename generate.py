"""Check every design cell/reference and emit typed C# rows. No game assets are extracted."""
import argparse, hashlib, json, pathlib, re, sys, zipfile

ROOT=pathlib.Path(__file__).resolve().parent
parser=argparse.ArgumentParser()
parser.add_argument('--jar',required=True)
parser.add_argument('--extra-jar',action='append',default=[],metavar='VERSION=PATH')
parser.add_argument('--allow-missing-local-assets',action='store_true',help='Allow absent optional clients and sounds in personal builds that use runtime fallbacks')
args=parser.parse_args()

# Official download metadata is a verification pin, never a packaged game asset.
OPTIONAL_SOURCES={'26.3':{'sha1':'e877b6a07acd633fb3bb475002175cec036e7b87','size':41483720},'1.21.2':{'sha1':'c7ac2d0d86f4ca416cab9064ff8a281852ad0c7b','size':27842947}}
OPTIONAL_SOURCES['1.21.9']={'sha1':'ce92fd8d1b2460c41ceda07ae7b3fe863a80d045','size':30591861}
extra_jars={}
for value in args.extra_jar:
    version,separator,path=value.partition('=')
    if not separator or not version or not path: parser.error('--extra-jar requires VERSION=PATH')
    extra_jars[version]=pathlib.Path(path)
tables={}
errors=[]
count=0
for path in sorted((ROOT/'sheets').glob('*.json')):
    table=json.loads(path.read_text(encoding='utf-8-sig'))
    name=table['name']
    if name in tables: errors.append(f'{path.name}: duplicate table {name}')
    tables[name]=table
    seen=set()
    for n,row in enumerate(table['rows']):
        if 'Id' not in row or row.get('Id') in seen: errors.append(f'{name}[{n}]: missing/duplicate Id')
        seen.add(row.get('Id'))
        for col,kind in table['columns'].items():
            count+=1
            value=row.get(col)
            if value is None or (isinstance(value,str) and not value.strip()):
                errors.append(f'{name}.{row.get("Id",n)}.{col}: unfilled')
            elif kind=='int' and (not isinstance(value,int) or isinstance(value,bool)):
                errors.append(f'{name}.{row.get("Id",n)}.{col}: expected int')
            elif kind=='float' and not isinstance(value,(float,int)):
                errors.append(f'{name}.{row.get("Id",n)}.{col}: expected float')
            elif kind=='bool' and not isinstance(value,bool):
                errors.append(f'{name}.{row.get("Id",n)}.{col}: expected bool')
            elif kind=='string' and not isinstance(value,str):
                errors.append(f'{name}.{row.get("Id",n)}.{col}: expected string')
        for extra in set(row)-set(table['columns']): errors.append(f'{name}.{row.get("Id",n)}: untyped {extra}')
for name,table in tables.items():
    for col,target in table.get('references',{}).items():
        target_table,target_col=target.split('.')
        if target_table not in tables:
            errors.append(f'{name}.{col}: target table {target_table} missing'); continue
        valid={row[target_col] for row in tables[target_table]['rows']}
        for row in table['rows']:
            if row.get(col) not in valid: errors.append(f'{name}.{row["Id"]}.{col}: unresolved {row.get(col)} -> {target}')
asset_rows=tables.get('assets',{}).get('rows',[])
source_versions={row.get('SourceJarVersion','primary') for row in asset_rows}
source_entries={}
source_proof={}
missing_optional=[]
mc_root=pathlib.Path(args.jar).resolve().parents[2]
for version in sorted(source_versions):
    if version=='primary': path=pathlib.Path(args.jar)
    else:
        candidates=(mc_root/'versions'/version/f'{version}.jar',ROOT.parent/'inspection'/'minecraft'/f'{version}-client.jar')
        path=extra_jars.get(version,next((candidate for candidate in candidates if candidate.is_file()),candidates[0]))
        if args.allow_missing_local_assets and version not in extra_jars and not path.exists():
            missing_optional.append(version)
            continue
        pin=OPTIONAL_SOURCES.get(version)
        if pin is None:
            errors.append(f'assets source {version}: no verified optional-client metadata'); continue
        try:
            digest=hashlib.sha1()
            with path.open('rb') as file:
                for chunk in iter(lambda:file.read(1024*1024),b''): digest.update(chunk)
            if path.stat().st_size!=pin['size'] or digest.hexdigest()!=pin['sha1']:
                errors.append(f'assets source {version}: size/SHA-1 verification failed'); continue
        except OSError as error:
            errors.append(f'assets source {version}: unavailable; supply --extra-jar {version}=PATH ({error})'); continue
        source_proof[version]={'sha1':pin['sha1'],'size':pin['size']}
    try:
        with zipfile.ZipFile(path) as jar: source_entries[version]=set(jar.namelist())
    except (OSError,zipfile.BadZipFile) as error:
        errors.append(f'assets source {version}: cannot read client archive ({error})')
entry_sources={}
for row in asset_rows:
    version=row.get('SourceJarVersion','primary')
    prior=entry_sources.setdefault(row['Entry'],version)
    if prior!=version: errors.append(f'assets.{row["Id"]}: entry assigned to conflicting client sources')
    if version in source_entries and row['Entry'] not in source_entries[version]:
        errors.append(f'assets.{row["Id"]}: missing from Minecraft source {version}: {row["Entry"]}')
sound_proof=[]
missing_sounds=[]
for row in tables.get('sound_sources',{}).get('rows',[]):
    found=(mc_root/row['LegacyPath']).is_file()
    if not found:
        for index in (mc_root/'assets'/'indexes').glob('*.json'):
            entry=json.loads(index.read_text(encoding='utf-8-sig')).get('objects',{}).get(row['Entry'],{})
            digest=entry.get('hash','')
            if re.fullmatch('[0-9a-f]{40}',digest) and (mc_root/'assets'/'objects'/digest[:2]/digest).is_file():
                found=True; break
    if found: sound_proof.append(row['Id'])
    elif args.allow_missing_local_assets: missing_sounds.append(row['Id'])
    else: errors.append(f'sound_sources.{row["Id"]}: declared local sound is not installed')
if errors:
    print('PREFLIGHT FAILED\n'+'\n'.join(errors)); sys.exit(1)

def singular(name):
    return {'weapons':'Weapon','assets':'Asset','systems':'System','resources':'Resource','cameras':'Camera','avatar':'Avatar','hooks':'Hook','buildings':'Building','tests':'Test','settings':'Setting','releases':'Release','sound_sources':'SoundSource','held_items':'HeldItem'}.get(name,name.title().rstrip('s'))
def literal(value,kind):
    if kind=='string': return json.dumps(value,ensure_ascii=True)
    if kind=='bool': return 'true' if value else 'false'
    if kind=='float': return repr(float(value))+'f'
    return str(value)

code=['// Generated by generate.py from checked JSON design sheets. Edit the sheets, then regenerate.','using System;','namespace CultCraft {']
for name,table in tables.items():
    typename=singular(name)+'Row'
    code.append(f'public struct {typename} {{')
    for col,kind in table['columns'].items(): code.append(f'public {kind} {col};')
    code.append('}')
code.append('public static class Spec {')
if 'releases' in tables:
    release=tables['releases']['rows'][0]
    for field in ('Version','Title','Guid'):
        code.append(f'public const string {field} = {literal(release[field],"string")};')
    code.append(f'public const string LoaderVersion = {literal(release["Version"].split("-")[0],"string")};')
for name,table in tables.items():
    stem=singular(name); typename=stem+'Row'
    code.append(f'public static readonly {typename}[] {stem}Rows = new {typename}[] {{')
    for row in table['rows']:
        code.append(f'new {typename} {{ '+', '.join(f'{col} = {literal(row[col],kind)}' for col,kind in table['columns'].items())+' },')
    code.append('};')
    code.append(f'public static {typename} {stem}(string id) {{ foreach(var row in {stem}Rows) if(row.Id==id) return row; throw new ArgumentException("Unknown {name}: "+id); }}')
code+=['}','}']
(ROOT/'src'/'Spec.Generated.cs').write_text('\n'.join(code)+'\n',encoding='utf-8')
report={'tables':len(tables),'rows':sum(len(t['rows']) for t in tables.values()),'cells':count,'unfilled':[],'unresolved':[],'assetEntries':'all present in verified client sources','optionalSources':source_proof,'optionalSourcesNotInstalled':missing_optional,'localSounds':sound_proof,'localSoundsNotInstalled':missing_sounds,'verification':'Design/static preflight; runtime gameplay is verified separately by Melty tests'}
(ROOT/'preflight.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('PREFLIGHT CLEAN: '+json.dumps(report,ensure_ascii=False))
