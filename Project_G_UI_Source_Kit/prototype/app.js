/* Vanilla JS prototype. No engine, server, analytics, accounts, payments or CDN.
 * Public interface: window.PGDemo.getState(), window.PGDemo.showScreen(name).
 * State is illustrative, local and deliberately not a game authority.
 */
(() => {
  'use strict';
  const $ = (q, scope = document) => scope.querySelector(q);
  const $$ = (q, scope = document) => [...scope.querySelectorAll(q)];
  const root = $('#screen');
  const HINT = {
    hud: 'Попробуйте меню C / B / N / J / M, команды Эхо и навыки. Tab в браузере перемещает фокус; боевой блок здесь не симулируется.',
    character: 'Характеристики отделены от ранга. Откройте источники модификаторов; скрытые профессии здесь не перечисляются.',
    inventory: 'Выберите предмет, попробуйте поиск, категории, сравнение, экипировку и защиту от случайной продажи.',
    echoes: 'Эхо — спутники, а не переключаемые герои. Доступны вкладки, отзыв из отряда и пример разговора.',
    guild: 'Выберите поручение, изучите условия и примите контракт. Ранг, местное доверие и уровень героя — разные вещи.',
    map: 'Слои карты и личная заметка. Неизвестные места не раскрываются; кнопки телепортации нет.',
    journal: 'Источники отделены от подтверждённых фактов. Можно закрепить поручение или открыть известную область.',
    dialogue: 'Выберите реплику: регистрация, правила рангов или доступные поручения. Это преднаписанный диалог, не AI.',
    components: 'Базовые цвета, типографика, состояния кнопок, 40 SVG-иконок и семантические статусы.'
  };
  const ITEMS = [
    {name:'Дорожный клинок +3',cat:'weapon',type:'МЕЧ · АВТОРСКАЯ РАБОТА',origin:'Работа мастера Талена',damage:148,defense:-12,durability:'68 / 80'},
    {name:'Клинок стражи +2',cat:'weapon',type:'МЕЧ · ОБЫЧНЫЙ',origin:'Снаряжение городской стражи',damage:136,defense:4,durability:'73 / 90'},
    {name:'Щит дозора',cat:'armor',type:'ЩИТ · ОБЫЧНЫЙ',origin:'Работа городской мастерской',damage:0,defense:22,durability:'91 / 100'},
    {name:'Шлем странника',cat:'armor',type:'ШЛЕМ · ОБЫЧНЫЙ',origin:'Работа городской мастерской',damage:0,defense:8,durability:'58 / 60'},
    {name:'Лесная трава',cat:'material',type:'МАТЕРИАЛ · ТРАВА',origin:'Собрана у лесной дороги',damage:null,defense:null,durability:'Не применяется'},
    {name:'Малое зелье',cat:'supply',type:'РАСХОДУЕМЫЙ · ЗЕЛЬЕ',origin:'Изготовлено в лечебнице',damage:null,defense:null,durability:'Не применяется'},
    {name:'Осколок памяти',cat:'material',type:'МАТЕРИАЛ · РЕЗОНАНС',origin:'Демонстрационный материал',damage:null,defense:null,durability:'Не применяется'},
    {name:'Сапоги следопыта',cat:'armor',type:'ОБУВЬ · ОБЫЧНАЯ',origin:'Работа мастера Талена',damage:0,defense:3,durability:'42 / 50'},
    {name:'Полевой дневник',cat:'other',type:'ЗАПИСЬ · ЛИЧНОЕ',origin:'Заметки путешественника',damage:null,defense:null,durability:'Не применяется'},
    {name:'Свет Старого пути',cat:'weapon',type:'АРТЕФАКТ · ЛЕГЕНДАРНЫЙ',origin:'Легендарный предмет · иллюстрация правила',damage:142,defense:0,durability:'80 / 80',legendary:true},
    {name:'Медный знак',cat:'other',type:'ТРОФЕЙ',origin:'Демонстрационный предмет',damage:null,defense:null,durability:'Не применяется'},
    {name:'Карта переправы',cat:'other',type:'КАРТА · ИЗВЕСТНЫЕ СВЕДЕНИЯ',origin:'Составлена персонажем',damage:null,defense:null,durability:'Не применяется'}
  ];
  const QUESTS = [
    {name:'Лесная дорога',rank:'F',body:['Смотритель давно не присылал отчётов.','Доставьте материалы и выясните, что случилось.'],known:['Последнее сообщение пришло со старой лесной дороги.','Точное местонахождение смотрителя не подтверждено.'],terms:['Оплата за доставку и подтверждённый отчёт.','Обнаружив неизвестную угрозу, сообщите в отделение.'],reward:'180 монет'},
    {name:'Лекарства для лечебницы',rank:'F',body:['Лечебнице нужны распространённые лесные травы.','Сдайте материалы, пока потребность не закрыта.'],known:['Заказчик — городская лечебница.','Открытое поручение: несколько исполнителей.'],terms:['Приём ограничен текущей потребностью заказчика.','Оплата за подтверждённую передачу материалов.'],reward:'90 монет'},
    {name:'Пропавший караван',rank:'E',body:['Караван не прибыл на назначенную стоянку.','Нужно найти следы и сообщить о его судьбе.'],known:['Последняя известная стоянка — у северной дороги.','Причина исчезновения не подтверждена.'],terms:['Для самостоятельного контракта требуется ранг E.','Участие младшего ранга возможно в согласованной роли.'],reward:'420 монет'},
    {name:'Следы у старой мельницы',rank:'D',body:['Жители сообщают о необычных следах и пропаже скота.','Проверьте сведения, не рискуя без необходимости.'],known:['Сведения получены от жителей пригородов.','Вид и сила предполагаемой угрозы неизвестны.'],terms:['Для самостоятельного контракта требуется ранг D.','Отчёт о новой опасности — полезный результат.'],reward:'640 монет'}
  ];
  let state, lastFocus, toastTimer;
  const timers = new Map();
  function reset() { state={screen:'hud',item:0,filter:'all',search:'',equipped:1,locked:new Set(),accepted:false,acceptedQuests:new Set(),quest:0,pinned:false,pvp:false,summoned:true,points:3,registered:true,layers:[true,true,true],pin:null}; }
  function escape(s) { return String(s).replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch])); }
  function replaceText(before, after, scope=root) { for(const node of $$('text',scope)) if(node.textContent===before) node.textContent=after; }
  function showScreen(name) { if(!window.UI_SCREENS[name]) return; state.screen=name; render(); }
  function render() {
    root.innerHTML=window.UI_SCREENS[state.screen];
    root.setAttribute('aria-label',HINT[state.screen]);
    $$('.screen-nav [data-screen]').forEach(btn=>{ if(btn.dataset.screen===state.screen)btn.setAttribute('aria-current','page');else btn.removeAttribute('aria-current'); });
    $('#screen-hint').textContent=HINT[state.screen];
    if(state.screen==='hud' && state.pvp) replaceText('PvP-тег выключен','PvP-тег включён');
    if(state.screen==='inventory') decorateInventory();
    if(state.screen==='guild') decorateQuest();
    if(state.screen==='echoes' && !state.summoned) { replaceText('Отозвать Эхо','Призвать Эхо');replaceText('В отряде','В коллекции');replaceText('Призвано: 2 / 3','Призвано: 1 / 3'); }
    if(state.screen==='character' && state.points===0) { replaceText('Распределить 3 очка','Все очки распределены');replaceText('42','45'); }
    if(state.screen==='journal' && state.pinned) replaceText('Закрепить в HUD','Закреплено в HUD');
    if(state.screen==='map') {
      const area=$('#rumor-layer',root);if(area)area.style.visibility=state.layers[2]?'visible':'hidden'; const settlements=$('#settlement-layer',root);if(settlements)settlements.style.visibility=state.layers[0]?'visible':'hidden';
      state.layers.forEach((on,i)=>{const group=$(`[data-action="map-layer:${i}"]`,root);if(group){group.style.opacity=on?'1':'.45';group.setAttribute('aria-pressed',String(on));}});
      if(state.pin && state.layers[1]) { const svg=$('svg',root),g=document.createElementNS('http://www.w3.org/2000/svg','g');g.innerHTML='<circle cx="724" cy="572" r="9" fill="#274B64" stroke="#FFF8E8" stroke-width="3"/><rect x="747" y="551" width="250" height="40" rx="6" fill="#FFF8E8" stroke="#B68A43"/><text x="760" y="578" font-family="Arial,sans-serif" font-size="18" fill="#273D49">'+escape(state.pin)+'</text>';svg.append(g); }
    }
    for(const [i,until] of timers) if(until>Date.now()) markSkill(i,Math.ceil((until-Date.now())/1000));
    $$('.interactive',root).forEach(g=>g.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();act(g.dataset.action);}}));
  }
  function decorateInventory() {
    const item=ITEMS[state.item];
    const itemIcon=$('#item-'+state.item+' g',root),art=$('#item-art',root); if(itemIcon&&art){const copy=itemIcon.cloneNode(true);copy.setAttribute('transform','translate(1122 251) scale(1.4166667)');copy.setAttribute('stroke','#F3D99B');art.replaceChildren(copy);}
    $('#item-title',root).textContent=item.name;$('#item-category',root).textContent=item.type;$('#item-origin',root).textContent=item.origin;
    replaceText('148',item.damage==null?'—':String(item.damage));replaceText('−12',item.defense==null?'—':String(item.defense).replace('-','−'));replaceText('68 / 80',item.durability);
    if(state.item!==0) {replaceText('Лёгкий клинок для долгих путешествий.',item.legendary?'Артефакт с собственной историей.':'Предмет из демонстрационного набора.');replaceText('Цена точного удара — меньшая защита.','Точные свойства проверяются игровым сервером.');}
    if(item.legendary){ replaceText('Не легендарный предмет. Может выпасть','Легендарный предмет. Не выпадает в PvP.');replaceText('при PvP-смерти с включённым тегом.','Это не гарантирует неразрушимость при улучшении.'); }
    else if(!['weapon','armor'].includes(item.cat)) {replaceText('Не легендарный предмет. Может выпасть','Правила потери этой категории ещё не определены.');replaceText('при PvP-смерти с включённым тегом.','В прототипе передача и расход не симулируются.');replaceText('Экипировать','Сведения');}
    if(state.locked.has(state.item))replaceText('Защитить от случайной продажи','Снять защиту от продажи');
    for(let i=0;i<ITEMS.length;i++){
      const group=$('#item-'+i,root);if(!group)continue;const first=$('rect',group);first.setAttribute('stroke',i===state.item?'#B68A43':'#C6B28A');first.setAttribute('stroke-width',i===state.item?'3':'1');
      const show=(state.filter==='all'||ITEMS[i].cat===state.filter)&&ITEMS[i].name.toLocaleLowerCase('ru').includes(state.search.toLocaleLowerCase('ru'));
      group.classList.toggle('filtered-out',!show);group.setAttribute('tabindex',show?'0':'-1');group.setAttribute('aria-pressed',String(state.item===i));
    }
  }
  function decorateQuest() {
    const q=QUESTS[state.quest],base=QUESTS[0],detail=$('#layer-5',root);
    replaceText(base.name,q.name,detail);
    for(let i=0;i<2;i++){replaceText(base.body[i],q.body[i],detail);replaceText(base.known[i],q.known[i],detail);replaceText(base.terms[i],q.terms[i],detail);}
    replaceText(base.reward,q.reward,detail);
    // Scope the rank change to the contract panel; retain the player's F badge.
    const layer=$('#layer-5',root);if(layer)for(const n of $$('text',layer))if(n.textContent==='Ранг F')n.textContent='Ранг '+q.rank;
    if(state.acceptedQuests.has(state.quest))replaceText('Принять поручение','Поручение принято');
  }
  function toast(message) { clearTimeout(toastTimer);const el=$('#toast');el.textContent=message;el.hidden=false;toastTimer=setTimeout(()=>el.hidden=true,3300); }
  function closeModal() { $('#modal-backdrop').hidden=true; if(lastFocus&&document.contains(lastFocus))lastFocus.focus(); }
  function modal(title, content, actions=[],setup) {
    lastFocus=document.activeElement;$('#modal-title').textContent=title;$('#modal-body').innerHTML=content;$('#modal-actions').replaceChildren();
    for(const a of actions){const b=document.createElement('button');b.textContent=a.label;b.className=a.style||'';b.addEventListener('click',()=>{if(a.run)a.run();else closeModal();});$('#modal-actions').append(b);}
    $('#modal-backdrop').hidden=false;if(setup)setup();setTimeout(()=>{$('input',$('#modal'))?.focus()||$('#modal').focus();},0);
  }
  function markSkill(index,seconds){const group=$(`[data-action="skill:${index}"]`,root);if(!group)return;group.style.opacity='.58';group.setAttribute('aria-label','Перезарядка: '+seconds+' секунд');}
  function act(action) {
    if(!action)return;const [type,value]=action.split(':');
    if(type==='screen'){showScreen(value);return;}
    if(type==='skill') {const i=Number(value);if((timers.get(i)||0)>Date.now())return;timers.set(i,Date.now()+3000);markSkill(i,3);toast('Демонстрация навыка '+('QWERASDF'[i])+': перезарядка 3 секунды.');setTimeout(()=>{timers.delete(i);if(state.screen==='hud')render();},3050);return;}
    if(type==='item'){state.item=Number(value);render();return;}
    if(type==='filter'){state.filter=value;state.search='';render();toast('Фильтр: '+({all:'все вещи',weapon:'оружие',armor:'броня',material:'материалы'}[value]||value));return;}
    if(type==='quest'){state.quest=Number(value);render();return;}
    if(type==='echo') {toast('Команда Эхо '+(Number(value)+1)+' отправлена в локальное демо. Герой не переключается.');return;}
    if(type==='choose-echo'){if(value==='0'){render();return;}modal(value==='1'?'Каэн':'Торен','<p>Дополнительный персонаж показан как пример состава. Детальный экран в этом наборе подготовлен для Сиэль.</p>',[{label:'Вернуться'}]);return;}
    if(type==='echo-tab') {
      const entries={overview:['Обзор','Личность, знания и состояние Эхо.'],skills:['Навыки','Путеводная звезда: пример сильной способности. Числа и правила боевого применения не заданы этим макетом.'],memory:['Воспоминания','Открытое воспоминание: старая переправа. Здесь показываются только уже известные записи, без списка скрытых сцен и общего счётчика секретов.'],relation:['Связь','Сиэль доверяет персонажу. Состояние зависит от совместной истории, а не от отдельной обязательной шкалы подарков.']};
      const [title,body]=entries[value];modal(title,'<p>'+body+'</p>',[{label:'Закрыть'}]);return;
    }
    if(type==='map-layer'){state.layers[Number(value)]=!state.layers[Number(value)];render();toast('Видимость слоя изменена.');return;}
    if(type==='journal-category'){if(value==='contracts'){showScreen('guild');return;}if(value==='current'){render();return;}modal(value==='rumors'?'Слухи':'Открытия',value==='rumors'?'<p>Огни за мельницей — неподтверждённый рассказ торговца.</p>':'<p>Старая переправа — подтверждённая личная запись.</p>',[{label:'Закрыть'}]);return;}
    if(type==='journal-entry'){if(value==='0'){render();return;}modal(value==='1'?'Огни за мельницей':'Старая переправа',value==='1'?'<p><strong>Источник:</strong> рассказ торговца. Сведения не подтверждены. Точных координат нет.</p>':'<p><strong>Источник:</strong> личное наблюдение. Переправа существует; запись не открывает соседние неизученные области.</p>',[{label:'Закрыть'}]);return;}
    switch(action) {
      case 'search': modal('Поиск предметов','<label for="item-search">Название предмета</label><input id="item-search" type="search" autocomplete="off" placeholder="Например, клинок"><div class="result-list" id="search-results"></div>',[{label:'Сбросить',run(){state.search='';state.filter='all';closeModal();render();}}],()=>{
        const field=$('#item-search');const update=()=>{const term=field.value.toLowerCase();$('#search-results').replaceChildren();ITEMS.forEach((item,i)=>{if(!item.name.toLowerCase().includes(term))return;const b=document.createElement('button');b.className='result-button';b.textContent=item.name;b.onclick=()=>{state.item=i;state.search=field.value;state.filter='all';closeModal();render();};$('#search-results').append(b);});if(!$('#search-results').children.length)$('#search-results').textContent='Ничего не найдено. Попробуйте другое название.';};field.addEventListener('input',update);update();});break;
      case 'compare': {
        const a=ITEMS[state.item],b=ITEMS[state.equipped];if(a.cat!=='weapon'){toast('В этом демо сравнение подготовлено для оружия.');break;}
        const delta=(x)=>`${x>0?'+':''}${x}`;modal('Сравнение с надетым',`<p><strong>${escape(a.name)}</strong><br>Сейчас надето: ${escape(b.name)}</p><table><tr><td>Урон</td><td>${a.damage} (${delta(a.damage-b.damage)})</td></tr><tr><td>Модификатор защиты</td><td>${a.defense} (${delta(a.defense-b.defense)})</td></tr></table><p class="help" style="margin-top:18px">Плюс или минус — изменение конкретного свойства, а не универсальная оценка «лучше / хуже». Базовые характеристики не становятся отрицательными.</p>`,[{label:'Закрыть'},{label:'Экипировать',style:'primary',run(){state.equipped=state.item;closeModal();toast('Оружие изменено только в локальном прототипе.');}}]);break;
      }
      case 'equip': if(ITEMS[state.item].cat!=='weapon'){toast('В этом демо экипировка реализована только для оружия.');break;}state.equipped=state.item;toast('Экипировано в демо: '+ITEMS[state.item].name);break;
      case 'lock-item': if(state.locked.has(state.item))state.locked.delete(state.item);else state.locked.add(state.item);render();toast('Изменена защита от случайной продажи. Правила PvP-дропа прежние.');break;
      case 'accept-quest': {
        const q=QUESTS[state.quest];if(q.rank!=='F'){modal('Требуется подтверждённый ранг','<p>Для самостоятельного принятия этого контракта нужен ранг '+q.rank+'. Это ограничение договора, а не запрет входить в регион.</p><p class="help">Правила участия в составе более опытной группы требуют отдельного игрового решения.</p>',[{label:'Понятно'}]);break;}
        if(state.acceptedQuests.has(state.quest)){toast('Поручение уже принято.');break;}
        modal('Принять поручение?',`<p><strong>${escape(q.name)}</strong><br>${escape(q.terms[0])}</p><p class="help">Информация может быть неполной. Принятие здесь меняет только локальное состояние макета, не создаёт игровой контракт.</p>`,[{label:'Отмена'},{label:'Принять',style:'primary',run(){state.acceptedQuests.add(state.quest);if(state.quest===0)state.accepted=true;closeModal();render();toast('Поручение принято в демо.');}}]);break;
      }
      case 'rank': modal('Ранги авантюристов','<p><strong>F → E → D → C → B → A → S</strong></p><p>Ранг отражает подтверждённую квалификацию и доверие организации. Он не равен уровню персонажа, профессии или положению в гильдии игроков.</p><p class="help">Повышение основано на послужном списке и испытаниях. Точные требования ещё предстоит определить; фиктивной полоски «убей 100 крыс до S» нет.</p>',[{label:'Понятно'}]);break;
      case 'service-record': modal('Послужной список','<p>Демонстрационная запись: помог лечебнице с поставками.</p><p class="help">Здесь будут подтверждённые действия и результаты, а не вся скрытая история мира.</p>',[{label:'Закрыть'}]);break;
      case 'pvp': modal('PvP-тег и риск имущества',`<p>Сейчас в демо тег <strong>${state.pvp?'включён':'выключен'}</strong>. Территория остаётся безопасной независимо от этой кнопки.</p><p>Вне защищённых зон выключенный тег не обязательно защищает от нападения. Он исключает выпадение экипировки согласно принятому концепту.</p><p class="help">С включённым тегом при PvP-смерти возможна потеря нелегендарной экипировки. Легендарные предметы не выпадают в PvP. Конкретные шансы и таймеры здесь не задаются.</p>`,[{label:'Закрыть'},{label:(state.pvp?'Выключить':'Включить')+' только в демо',style:state.pvp?'primary':'danger',run(){state.pvp=!state.pvp;closeModal();render();toast('Это локальная демонстрация. Правила безопасной зоны не изменены.');}}]);break;
      case 'modifiers': modal('Откуда взялась защита','<table><tr><td>Экипировка и известные эффекты</td><td>+4</td></tr><tr><td>Свойство дорожного клинка</td><td>−16</td></tr><tr><td>Итого</td><td>−12</td></tr></table><p class="help" style="margin-top:18px">Числа демонстрационные. Сервер передаёт разрешённые для показа источники, но не условия скрытых профессий.</p>',[{label:'Закрыть'}]);break;
      case 'stat-points': if(!state.points){toast('В демо свободных очков больше нет.');break;}modal('Распределение характеристик','<p>Пример предпросмотра: Сила <strong>42 → 45</strong>. Будет потрачено 3 очка.</p><p class="help">Это не смена профессии и не расход очков навыков. Возможность возврата очков в игре этим экраном не определяется.</p>',[{label:'Отмена'},{label:'Подтвердить в демо',style:'primary',run(){state.points=0;closeModal();render();}}]);break;
      case 'summon': state.summoned=!state.summoned;render();toast(state.summoned?'Сиэль призвана в демо.':'Сиэль отозвана в демо.');break;
      case 'talk-echo': modal('Сиэль','<p>«Эти знаки ставили вдоль дорог Старого королевства. Странно видеть их так близко к городу…»</p><p class="help">Заранее написанная реплика. Она даёт контекст, но не открывает координаты секрета.</p>',[{label:'Продолжить путь'}]);break;
      case 'pin-quest': state.pinned=true;render();toast('Поручение закреплено в игровом HUD.');break;
      case 'add-pin': modal('Личная отметка','<label for="pin-title">Подпись отметки</label><input id="pin-title" maxlength="24" placeholder="Например, место встречи"><p class="help">В демо отметка ставится на фиксированную известную точку. Выбор координат и торговля картами здесь не реализованы.</p>',[{label:'Отмена'},{label:'Добавить',style:'primary',run(){const v=$('#pin-title').value.trim();if(!v){$('#pin-title').focus();return;}state.pin=v;closeModal();render();}}]);break;
      case 'register': modal('Регистрация авантюриста','<p>Вступление добровольное. Начальный ранг — <strong>F</strong>. Членство не заменяет профессию или гильдию игроков.</p><p class="help">Другие экраны уже показывают демо-персонажа после регистрации. Здесь показан отдельный сценарий подтверждения.</p>',[{label:'Пока не вступать'},{label:'Зарегистрироваться',style:'primary',run(){state.registered=true;closeModal();showScreen('guild');toast('Удостоверение F-ранга создано только в демо.');}}]);break;
      case 'chat': modal('Сообщение группе','<label for="chat-text">Текст сообщения</label><input id="chat-text" maxlength="120" placeholder="Встречаемся у моста"><p class="help">Сообщение не отправляется другим людям. Сетевого чата в прототипе нет.</p>',[{label:'Отмена'},{label:'Показать в демо',style:'primary',run(){const v=$('#chat-text').value.trim();if(!v)return;closeModal();toast('Вы: '+v);}}]);break;
      default: toast('Этот переход пока показан только в макете.');
    }
  }
  root.addEventListener('click',e=>{const group=e.target.closest('[data-action]');if(group&&!group.classList.contains('filtered-out'))act(group.dataset.action);});
  $$('.screen-nav [data-screen]').forEach(b=>b.addEventListener('click',()=>showScreen(b.dataset.screen)));
  $('#modal-close').addEventListener('click',closeModal);
  $('#modal-backdrop').addEventListener('click',e=>{if(e.target===e.currentTarget)closeModal();});
  $('#reset-demo').addEventListener('click',()=>{timers.clear();reset();closeModal();render();toast('Демонстрация сброшена.');});
  document.addEventListener('keydown',e=>{
    if(!$('#modal-backdrop').hidden){
      if(e.key==='Escape'){e.preventDefault();closeModal();return;}
      if(e.key==='Tab'){const nodes=$$('button,input,[tabindex="0"]',$('#modal')).filter(n=>!n.disabled);const first=nodes[0],last=nodes.at(-1);if(e.shiftKey&&(document.activeElement===first||document.activeElement===$('#modal'))){e.preventDefault();last?.focus();}else if(!e.shiftKey&&(document.activeElement===last||document.activeElement===$('#modal'))){e.preventDefault();first?.focus();}}return;
    }
    if(e.ctrlKey||e.altKey||e.metaKey||e.target.matches('input,textarea,[contenteditable]'))return;
    if(e.code==='Escape'){e.preventDefault();showScreen('hud');return;}
    const screens={KeyC:'character',KeyB:'inventory',KeyN:'echoes',KeyJ:'journal',KeyM:'map'};
    if(screens[e.code]){e.preventDefault();showScreen(state.screen===screens[e.code]?'hud':screens[e.code]);return;}
    if(state.screen==='hud'){
      const codes=['KeyQ','KeyW','KeyE','KeyR','KeyA','KeyS','KeyD','KeyF'],index=codes.indexOf(e.code);if(index>=0){e.preventDefault();act('skill:'+index);}
      if(['Digit1','Digit2','Digit3'].includes(e.code)){e.preventDefault();act('echo:'+(Number(e.code.slice(-1))-1));}
    }
  });
  reset();render();
  window.PGDemo={showScreen,getState:()=>({...state,locked:[...state.locked],acceptedQuests:[...state.acceptedQuests]}),reset:()=>{reset();render();}};
})();
