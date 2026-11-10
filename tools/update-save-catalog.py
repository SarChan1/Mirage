"""Generate a compact Windows save-location catalog. No credentials or user files."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import urllib.request
import yaml

SOURCE = 'https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml'

def windows_save(rule):
    return 'save' in rule.get('tags', []) and (not rule.get('when') or any(condition.get('os', 'windows') == 'windows' for condition in rule['when']))

def convert(data):
    result = []
    aliases_by_name = {}
    for alias, entry in data.items():
        if isinstance(entry.get('alias'), str):
            aliases_by_name.setdefault(entry['alias'], []).append(alias)
    for name, entry in data.items():
        if 'alias' in entry:
            continue
        paths = [path for path, rule in entry.get('files', {}).items() if windows_save(rule)]
        registry = [path for path, rule in entry.get('registry', {}).items() if windows_save(rule)]
        if not paths and not registry:
            continue
        ids = [entry.get('steam', {}).get('id'), *entry.get('id', {}).get('steamExtra', [])]
        aliases = aliases_by_name.get(name, [])
        result.append(dict(name=name, aliases=aliases, steamIds=[str(value) for value in ids if value], installDirs=list(entry.get('installDir', {})), paths=paths, registry=registry))
    return result

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', type=Path)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'source' / 'catalog.saves.json')
    args = parser.parse_args()
    if args.input:
        content = args.input.read_bytes()
    else:
        with urllib.request.urlopen(SOURCE, timeout=45) as response:
            content = response.read(30_000_001)
    if len(content) > 30_000_000:
        raise ValueError('Manifest exceeds size limit')
    data = yaml.load(content, Loader=getattr(yaml, 'CSafeLoader', yaml.SafeLoader))
    games = convert(data)
    if not games:
        raise ValueError('Empty save catalog')
    catalog = dict(schemaVersion=1, source='Ludusavi manifest', sourceUrl=SOURCE, updatedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(), sourceSha256=hashlib.sha256(content).hexdigest(), games=games)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix('.tmp')
    temporary.write_text(json.dumps(catalog, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    temporary.replace(args.output)
    print(f'Save catalog: {len(games)} Windows games, {sum(len(g["paths"]) for g in games)} path rules')

if __name__ == '__main__':
    main()
