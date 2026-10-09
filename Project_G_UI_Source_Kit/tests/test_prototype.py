"""UI smoke tests. Optional dependency: playwright with a Chromium executable.
Run: CHROMIUM_PATH=/path/to/chromium python tests/test_prototype.py
No page navigation needed: load the self-contained preview into a blank document.
"""
import asyncio, json, os
from pathlib import Path
from playwright.async_api import async_playwright
ROOT=Path(__file__).resolve().parents[1]
results=[]
def record(name,ok,detail=''):
 results.append({'check':name,'passed':bool(ok),'detail':detail})
 if not ok: print('FAIL:',name,detail)

async def main():
 async with async_playwright() as p:
  path=os.environ.get('CHROMIUM_PATH','/usr/bin/chromium')
  browser=await p.chromium.launch(executable_path=path,headless=True,args=['--no-sandbox'])
  page=await browser.new_page(viewport={'width':1982,'height':1300})
  errors=[];requests=[]
  page.on('pageerror',lambda e:errors.append(str(e)));page.on('request',lambda r:requests.append(r.url))
  await page.set_content((ROOT/'Preview.html').read_text(),wait_until='load')
  record('Standalone preview initialized',await page.evaluate('Boolean(window.PGDemo)'))
  async def screen(name):await page.evaluate('(s)=>PGDemo.showScreen(s)',name)
  async def act(name):await page.locator(f'#screen [data-action="{name}"]').click()
  async def close():await page.locator('#modal-close').click()
  for name in ['hud','character','inventory','echoes','guild','map','journal','dialogue','components']:
   await screen(name)
   bad=await page.evaluate('''()=>[...document.querySelectorAll('#screen svg text')].filter(n=>{const b=n.getBBox();return b.x< -1||b.y < -1||b.x+b.width>1921||b.y+b.height>1081;}).map(n=>n.textContent)''')
   record('Text within canvas: '+name,not bad,str(bad))
  await screen('hud')
  record('Eight skill slots',await page.locator('[data-action^="skill:"]').count()==8)
  record('Three Echo command controls',await page.locator('[data-action^="echo:"]').count()==3)
  await page.keyboard.press('b');record('Keyboard B opens inventory',await page.evaluate("PGDemo.getState().screen==='inventory'"))
  await act('search');await page.locator('#item-search').fill('клинок');record('Search finds two swords',await page.locator('#search-results button').count()==2)
  await page.locator('#search-results button').first.click();record('Search result selects item',await page.evaluate('PGDemo.getState().item===0'))
  await act('filter:armor');record('Armor filter: three visible items',await page.locator('[id^="item-"][data-action]:not(.filtered-out)').count()==3)
  await act('filter:all');await act('item:9');record('Legendary PvP protection shown','Не выпадает в PvP' in await page.locator('#screen').inner_text())
  await act('lock-item');record('Sale lock toggled',9 in await page.evaluate('PGDemo.getState().locked'))
  await act('item:0');await act('compare');record('Comparison deltas shown','+12' in await page.locator('#modal-body').inner_text() and '-16' in await page.locator('#modal-body').inner_text())
  await page.locator('#modal-actions button.primary').click();record('Local equipment changed',await page.evaluate('PGDemo.getState().equipped===0'))
  await screen('guild');await act('quest:2');record('Quest selection preserves list title',await page.locator('[data-action="quest:0"]').text_content()!='' and 'Лесная дорога' in await page.locator('[data-action="quest:0"]').text_content())
  await act('accept-quest');record('Rank qualification warning shown','подтверждённый ранг' in await page.locator('#modal-title').inner_text());await close()
  await act('quest:0');await act('accept-quest');await page.locator('#modal-actions button.primary').click();record('Quest accepted after confirmation',await page.evaluate('PGDemo.getState().accepted'))
  await screen('journal');await act('pin-quest');record('Quest pinned',await page.evaluate('PGDemo.getState().pinned'))
  await screen('map');await act('map-layer:2');record('Entire rumor layer hides',await page.locator('#rumor-layer').evaluate("e=>getComputedStyle(e).visibility==='hidden'"))
  await act('map-layer:0');record('Settlement layer hides',await page.locator('#settlement-layer').evaluate("e=>getComputedStyle(e).visibility==='hidden'"))
  await act('add-pin');await page.locator('#pin-title').fill('<Заметка>');await page.locator('#modal-actions button.primary').click();record('User pin escaped safely','<Заметка>' in await page.locator('#screen').inner_text())
  await screen('echoes');await act('summon');record('Echo removed without hero switch',await page.evaluate('!PGDemo.getState().summoned'))
  await screen('hud');await act('chat');await page.locator('#chat-text').fill('b');record('Typing does not trigger gameplay shortcut',await page.evaluate("PGDemo.getState().screen==='hud'"))
  await page.locator('#modal-actions button').last.focus();await page.keyboard.press('Tab');record('Modal focus trapped',await page.evaluate("document.activeElement.id==='modal-close'"))
  await page.keyboard.press('Escape');record('Escape closes modal',await page.locator('#modal-backdrop').is_hidden())
  await screen('inventory');await page.keyboard.press('Escape');record('Escape returns to HUD',await page.evaluate("PGDemo.getState().screen==='hud'"))
  await screen('hud');await act('pvp');await page.locator('#modal-actions button.danger').click();record('PvP demo toggles explicitly',await page.evaluate('PGDemo.getState().pvp'))
  for w,h in [(1280,720),(1920,1080),(2560,1440),(3440,1440)]:
   await page.set_viewport_size({'width':w,'height':h});await screen('inventory')
   record(f'No horizontal browser overflow {w}x{h}',await page.evaluate('document.documentElement.scrollWidth<=innerWidth'))
  record('No external requests',not requests,str(requests[:3]));record('No JavaScript runtime errors',not errors,str(errors))
  await browser.close()
 (ROOT/'tests/prototype_results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
 print(sum(r['passed'] for r in results),'/',len(results),'checks passed')
 if any(not r['passed'] for r in results):raise SystemExit(1)
if __name__=='__main__':asyncio.run(main())
