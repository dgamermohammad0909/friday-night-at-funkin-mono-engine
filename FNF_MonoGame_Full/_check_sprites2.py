import json

data = open('Content/menus/character_select/bf/player/spritemap1.json', 'r', encoding='utf-8-sig').read()
d = json.loads(data)
for s in d['ATLAS']['SPRITES']:
    sp = s['SPRITE']
    name = sp['name']
    w = sp['w']
    h = sp['h']
    rot = sp.get('rotated', False)
    print(f'{name}: {w}x{h} rot={rot}')
